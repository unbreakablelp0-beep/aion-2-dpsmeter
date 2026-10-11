namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Netzwerkdiagnose: prüft die Aufnahme-Anbindung, listet Adapter, startet/stoppt
/// eine passive Aufnahme und zählt Pakete. Kennt Npcap nicht direkt, sondern nur
/// <see cref="IPacketCaptureBackend"/> – dadurch mit simulierten Paketen testbar.
/// </summary>
/// <remarks>
/// <see cref="StatusChanged"/> kann auf einem Hintergrundthread ausgelöst werden.
/// </remarks>
public sealed class NetworkDiagnostics
{
    private readonly IPacketCaptureBackend _backend;
    private readonly PacketCounter _counter = new();
    private readonly object _lock = new();

    private CaptureStatus _status = CaptureStatus.NotConfigured;
    private IReadOnlyList<NetworkAdapterInfo> _adapters = [];
    private CancellationTokenSource? _cancellation;
    private Task _completion = Task.CompletedTask;

    public NetworkDiagnostics(IPacketCaptureBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>Wird bei jeder Statusänderung ausgelöst.</summary>
    public event EventHandler<CaptureStatus>? StatusChanged;

    /// <summary>Aktueller Status.</summary>
    public CaptureStatus Status
    {
        get { lock (_lock) return _status; }
    }

    /// <summary>Zuletzt geladene Adapter.</summary>
    public IReadOnlyList<NetworkAdapterInfo> Adapters
    {
        get { lock (_lock) return _adapters; }
    }

    /// <summary>Ergebnis der letzten Verfügbarkeitsprüfung oder null.</summary>
    public CaptureBackendInfo? BackendInfo { get; private set; }

    /// <summary>Paketstatistik der aktuellen bzw. letzten Aufnahme.</summary>
    public PacketStatistics Statistics => _counter.Snapshot();

    /// <summary>
    /// Endet, wenn die aktuelle Aufnahme beendet ist. Aufnahmefehler landen im <see cref="Status"/>;
    /// die Aufgabe schlägt nur fehl, wenn ein <see cref="StatusChanged"/>-Handler eine Ausnahme wirft.
    /// </summary>
    public Task Completion
    {
        get { lock (_lock) return _completion; }
    }

    /// <summary>
    /// Prüft die Anbindung und lädt die Adapterliste (neu).
    /// </summary>
    /// <exception cref="InvalidOperationException">Während eine Aufnahme läuft.</exception>
    public CaptureStatus Initialize()
    {
        if (Status.State == CaptureState.Capturing)
        {
            throw new InvalidOperationException("Während einer laufenden Aufnahme nicht möglich.");
        }

        var info = _backend.GetInfo();
        BackendInfo = info;

        if (!info.IsAvailable)
        {
            return SetStatus(CaptureStatus.Unavailable(info.Error ?? CaptureErrorKind.Unknown, info.Detail), []);
        }

        IReadOnlyList<NetworkAdapterInfo> adapters;
        try
        {
            adapters = _backend.GetAdapters();
        }
        catch (CaptureException ex)
        {
            return SetStatus(CaptureStatus.Unavailable(ex.Kind, ex.Message), []);
        }
        catch (Exception ex)
        {
            return SetStatus(CaptureStatus.Unavailable(CaptureErrorKind.AdapterListFailed, ex.Message), []);
        }

        return adapters.Count == 0
            ? SetStatus(CaptureStatus.Unavailable(CaptureErrorKind.NoAdaptersFound), [])
            : SetStatus(CaptureStatus.Ready(adapters.Count), adapters);
    }

    /// <summary>
    /// Startet die Aufnahme auf dem Adapter und kehrt sofort zurück.
    /// Ein unbekannter Adapter führt zu <see cref="CaptureState.Faulted"/> mit
    /// <see cref="CaptureErrorKind.AdapterNotFound"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Wenn nicht bereit oder bereits eine Aufnahme läuft.</exception>
    public CaptureStatus Start(string adapterId)
    {
        ArgumentNullException.ThrowIfNull(adapterId);

        CaptureStatus status;
        Task<Task>? run = null;
        lock (_lock)
        {
            if (_status.State is not (CaptureState.Ready or CaptureState.Stopped or CaptureState.Faulted))
            {
                throw new InvalidOperationException(
                    $"Aufnahme kann im Zustand {_status.State} nicht gestartet werden. Zuerst Initialize() aufrufen.");
            }

            var adapter = _adapters.FirstOrDefault(a => a.Id == adapterId);
            if (adapter is null)
            {
                status = _status = CaptureStatus.Faulted(CaptureErrorKind.AdapterNotFound, adapterId);
            }
            else
            {
                _counter.Reset();
                var cancellation = _cancellation = new CancellationTokenSource();
                status = _status = CaptureStatus.Capturing(adapter);

                // Erst nach dem Capturing-Ereignis starten, damit die Reihenfolge der Statusmeldungen stimmt.
                run = new Task<Task>(() => RunAsync(adapterId, cancellation));
                _completion = run.Unwrap();
            }
        }

        try
        {
            StatusChanged?.Invoke(this, status);
        }
        finally
        {
            run?.Start(TaskScheduler.Default);
        }

        return status;
    }

    /// <summary>Stoppt die laufende Aufnahme und wartet, bis sie beendet ist.</summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        lock (_lock) cancellation = _cancellation;

        cancellation?.Cancel();
        await Completion.ConfigureAwait(false);
    }

    private async Task RunAsync(string adapterId, CancellationTokenSource cancellation)
    {
        CaptureStatus final;
        try
        {
            await _backend.CaptureAsync(adapterId, _counter.Add, cancellation.Token).ConfigureAwait(false);
            final = cancellation.IsCancellationRequested
                ? CaptureStatus.Stopped
                : CaptureStatus.Faulted(CaptureErrorKind.CaptureAborted);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            final = CaptureStatus.Stopped;
        }
        catch (CaptureException ex)
        {
            final = CaptureStatus.Faulted(ex.Kind, ex.Message);
        }
        catch (Exception ex)
        {
            final = CaptureStatus.Faulted(CaptureErrorKind.Unknown, ex.Message);
        }

        lock (_lock)
        {
            _status = final;
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
        }

        StatusChanged?.Invoke(this, final);
    }

    private CaptureStatus SetStatus(CaptureStatus status, IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        lock (_lock)
        {
            _status = status;
            _adapters = adapters;
        }

        StatusChanged?.Invoke(this, status);
        return status;
    }
}
