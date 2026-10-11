using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

/// <summary>
/// Tests der Netzwerkdiagnose mit simulierten Paketereignissen (kein echtes Netzwerk, kein Npcap).
/// </summary>
public class NetworkDiagnosticsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (NetworkDiagnostics Diagnostics, FakeCaptureBackend Backend) Create()
    {
        var backend = new FakeCaptureBackend();
        return (new NetworkDiagnostics(backend), backend);
    }

    /// <summary>Simuliert eine Aufnahme, die die Pakete sendet, meldet und dann bis zum Abbruch wartet.</summary>
    private static Func<string, Action<CapturedPacket>, CancellationToken, Task> SendThenWait(
        TaskCompletionSource sent, params CapturedPacket[] packets) =>
        async (_, onPacket, token) =>
        {
            foreach (var p in packets) onPacket(p);
            sent.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

    [Fact]
    public void NewInstance_IsNotConfiguredAndEmpty()
    {
        var (diagnostics, _) = Create();

        Assert.Equal(CaptureState.NotConfigured, diagnostics.Status.State);
        Assert.Equal(PacketStatistics.Empty, diagnostics.Statistics);
        Assert.Empty(diagnostics.Adapters);
    }

    [Fact]
    public void Initialize_WithAdapters_IsReady()
    {
        var (diagnostics, _) = Create();

        var status = diagnostics.Initialize();

        Assert.Equal(CaptureState.Ready, status.State);
        Assert.Equal("Bereit – 2 Netzwerkadapter gefunden", status.Message);
        Assert.Equal(2, diagnostics.Adapters.Count);
        Assert.Equal("Fake 1.0", diagnostics.BackendInfo?.Version);
    }

    [Fact]
    public void Initialize_NpcapMissing_IsUnavailableAndDoesNotListAdapters()
    {
        var (diagnostics, backend) = Create();
        backend.Info = new CaptureBackendInfo(false, null, CaptureErrorKind.NpcapNotInstalled, "wpcap.dll fehlt");

        var status = diagnostics.Initialize();

        Assert.Equal(CaptureState.Unavailable, status.State);
        Assert.Equal(CaptureErrorKind.NpcapNotInstalled, status.Error);
        Assert.Contains("Npcap", status.Message);
        Assert.Equal("wpcap.dll fehlt", status.Detail);
        Assert.Equal(0, backend.GetAdaptersCalls);
        Assert.Empty(diagnostics.Adapters);
    }

    [Fact]
    public void Initialize_NoPermissionReportedByBackend_IsAccessDenied()
    {
        var (diagnostics, backend) = Create();
        backend.Info = new CaptureBackendInfo(false, "1.89", CaptureErrorKind.AccessDenied);

        var status = diagnostics.Initialize();

        Assert.Equal(CaptureState.Unavailable, status.State);
        Assert.Equal(CaptureErrorKind.AccessDenied, status.Error);
    }

    [Fact]
    public void Initialize_UnavailableWithoutReason_IsUnknownError()
    {
        var (diagnostics, backend) = Create();
        backend.Info = new CaptureBackendInfo(false, null);

        Assert.Equal(CaptureErrorKind.Unknown, diagnostics.Initialize().Error);
    }

    [Fact]
    public void Initialize_AdapterListAccessDenied_IsAccessDenied()
    {
        var (diagnostics, backend) = Create();
        backend.GetAdaptersException = new CaptureException(CaptureErrorKind.AccessDenied, "Access is denied");

        var status = diagnostics.Initialize();

        Assert.Equal(CaptureState.Unavailable, status.State);
        Assert.Equal(CaptureErrorKind.AccessDenied, status.Error);
        Assert.Equal("Access is denied", status.Detail);
    }

    [Fact]
    public void Initialize_AdapterListUnexpectedError_IsAdapterListFailed()
    {
        var (diagnostics, backend) = Create();
        backend.GetAdaptersException = new InvalidOperationException("kaputt");

        var status = diagnostics.Initialize();

        Assert.Equal(CaptureErrorKind.AdapterListFailed, status.Error);
        Assert.Equal("kaputt", status.Detail);
    }

    [Fact]
    public void Initialize_NoAdapters_IsNoAdaptersFound()
    {
        var (diagnostics, backend) = Create();
        backend.AdapterList.Clear();

        var status = diagnostics.Initialize();

        Assert.Equal(CaptureState.Unavailable, status.State);
        Assert.Equal(CaptureErrorKind.NoAdaptersFound, status.Error);
    }

    [Fact]
    public void Start_BeforeInitialize_Throws()
    {
        var (diagnostics, _) = Create();

        Assert.Throws<InvalidOperationException>(() => diagnostics.Start(FakeCaptureBackend.Ethernet.Id));
    }

    [Fact]
    public void Start_WhenUnavailable_Throws()
    {
        var (diagnostics, backend) = Create();
        backend.Info = new CaptureBackendInfo(false, null, CaptureErrorKind.NpcapNotInstalled);
        diagnostics.Initialize();

        Assert.Throws<InvalidOperationException>(() => diagnostics.Start(FakeCaptureBackend.Ethernet.Id));
        Assert.Equal(0, backend.CaptureCalls);
    }

    [Fact]
    public void Start_UnknownAdapter_IsFaultedWithoutStartingCapture()
    {
        var (diagnostics, backend) = Create();
        diagnostics.Initialize();

        var status = diagnostics.Start(@"\Device\NPF_{GIBT-ES-NICHT}");

        Assert.Equal(CaptureState.Faulted, status.State);
        Assert.Equal(CaptureErrorKind.AdapterNotFound, status.Error);
        Assert.Equal(0, backend.CaptureCalls);
    }

    [Fact]
    public async Task Capture_CountsSimulatedPacketsAndStops()
    {
        var (diagnostics, backend) = Create();
        var sent = new TaskCompletionSource();
        backend.CaptureBehavior = SendThenWait(sent,
            new CapturedPacket(T0, 60),
            new CapturedPacket(T0.AddSeconds(1), 1500),
            new CapturedPacket(T0.AddSeconds(2), 40));
        diagnostics.Initialize();

        var started = diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await sent.Task.WaitAsync(TestTimeout);
        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Capturing, started.State);
        Assert.Equal("Aufnahme läuft: Simulierter Ethernet-Adapter", started.Message);
        Assert.Equal(CaptureState.Stopped, diagnostics.Status.State);
        Assert.Equal(new PacketStatistics(3, 1600, T0, T0.AddSeconds(2)), diagnostics.Statistics);
    }

    [Fact]
    public async Task StatusChanged_ReportsReadyCapturingStoppedInOrder()
    {
        var (diagnostics, _) = Create();
        var states = new List<CaptureState>();
        diagnostics.StatusChanged += (_, s) => { lock (states) states.Add(s.State); };

        diagnostics.Initialize();
        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal([CaptureState.Ready, CaptureState.Capturing, CaptureState.Stopped], states);
    }

    [Fact]
    public async Task Stop_BackendReturningNormallyOnCancel_IsStopped()
    {
        var (diagnostics, backend) = Create();
        backend.CaptureBehavior = async (_, _, token) =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { /* Backend beendet sich ohne Ausnahme */ }
        };
        diagnostics.Initialize();

        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Stopped, diagnostics.Status.State);
        Assert.Null(diagnostics.Status.Error);
    }

    [Fact]
    public async Task Capture_AdapterCannotBeOpened_IsFaulted()
    {
        var (diagnostics, backend) = Create();
        backend.CaptureBehavior = (_, _, _) =>
            Task.FromException(new CaptureException(CaptureErrorKind.AdapterOpenFailed, "Gerät belegt"));
        diagnostics.Initialize();

        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.Completion.WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Faulted, diagnostics.Status.State);
        Assert.Equal(CaptureErrorKind.AdapterOpenFailed, diagnostics.Status.Error);
        Assert.Equal("Gerät belegt", diagnostics.Status.Detail);
    }

    [Fact]
    public async Task Capture_BackendThrowsSynchronously_IsFaulted()
    {
        var (diagnostics, backend) = Create();
        backend.CaptureBehavior = (_, _, _) => throw new CaptureException(CaptureErrorKind.AccessDenied);
        diagnostics.Initialize();

        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.Completion.WaitAsync(TestTimeout);

        Assert.Equal(CaptureErrorKind.AccessDenied, diagnostics.Status.Error);
    }

    [Fact]
    public async Task Capture_UnexpectedException_IsFaultedUnknown()
    {
        var (diagnostics, backend) = Create();
        backend.CaptureBehavior = (_, _, _) => Task.FromException(new InvalidOperationException("boom"));
        diagnostics.Initialize();

        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.Completion.WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Faulted, diagnostics.Status.State);
        Assert.Equal(CaptureErrorKind.Unknown, diagnostics.Status.Error);
        Assert.Equal("boom", diagnostics.Status.Detail);
    }

    [Fact]
    public async Task Capture_EndsWithoutStop_IsAborted()
    {
        var (diagnostics, backend) = Create();
        backend.CaptureBehavior = (_, _, _) => Task.CompletedTask;
        diagnostics.Initialize();

        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.Completion.WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Faulted, diagnostics.Status.State);
        Assert.Equal(CaptureErrorKind.CaptureAborted, diagnostics.Status.Error);
    }

    [Fact]
    public async Task Capture_FailsAfterPackets_KeepsCountedPackets()
    {
        var (diagnostics, backend) = Create();
        backend.CaptureBehavior = (_, onPacket, _) =>
        {
            onPacket(new CapturedPacket(T0, 100));
            onPacket(new CapturedPacket(T0.AddSeconds(1), 200));
            return Task.FromException(new CaptureException(CaptureErrorKind.CaptureFailed, "Lesefehler"));
        };
        diagnostics.Initialize();

        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.Completion.WaitAsync(TestTimeout);

        Assert.Equal(CaptureErrorKind.CaptureFailed, diagnostics.Status.Error);
        Assert.Equal(2, diagnostics.Statistics.PacketCount);
        Assert.Equal(300, diagnostics.Statistics.ByteCount);
    }

    [Fact]
    public async Task Start_WhileCapturing_Throws()
    {
        var (diagnostics, _) = Create();
        diagnostics.Initialize();
        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);

        Assert.Throws<InvalidOperationException>(() => diagnostics.Start(FakeCaptureBackend.Loopback.Id));
        Assert.Throws<InvalidOperationException>(() => diagnostics.Initialize());

        await diagnostics.StopAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Restart_AfterStop_ResetsCounters()
    {
        var (diagnostics, backend) = Create();
        var firstSent = new TaskCompletionSource();
        backend.CaptureBehavior = SendThenWait(firstSent, new CapturedPacket(T0, 500), new CapturedPacket(T0, 500));
        diagnostics.Initialize();
        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await firstSent.Task.WaitAsync(TestTimeout);
        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        var secondSent = new TaskCompletionSource();
        backend.CaptureBehavior = SendThenWait(secondSent, new CapturedPacket(T0.AddMinutes(1), 70));
        var status = diagnostics.Start(FakeCaptureBackend.Loopback.Id);
        await secondSent.Task.WaitAsync(TestTimeout);
        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Capturing, status.State);
        Assert.Equal(new PacketStatistics(1, 70, T0.AddMinutes(1), T0.AddMinutes(1)), diagnostics.Statistics);
    }

    [Fact]
    public async Task Restart_AfterFault_IsAllowed()
    {
        var (diagnostics, backend) = Create();
        backend.CaptureBehavior = (_, _, _) => Task.FromException(new CaptureException(CaptureErrorKind.CaptureFailed));
        diagnostics.Initialize();
        diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.Completion.WaitAsync(TestTimeout);

        backend.CaptureBehavior = (_, _, token) => Task.Delay(Timeout.Infinite, token);
        var status = diagnostics.Start(FakeCaptureBackend.Ethernet.Id);
        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Capturing, status.State);
        Assert.Equal(CaptureState.Stopped, diagnostics.Status.State);
    }

    [Fact]
    public async Task StopAsync_WithoutCapture_DoesNothing()
    {
        var (diagnostics, _) = Create();
        diagnostics.Initialize();

        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Ready, diagnostics.Status.State);
    }

    [Fact]
    public async Task StatusChangedHandlerThrowing_DoesNotPreventStop()
    {
        var (diagnostics, _) = Create();
        diagnostics.Initialize();
        diagnostics.StatusChanged += (_, s) =>
        {
            if (s.State == CaptureState.Capturing) throw new InvalidOperationException("Handlerfehler");
        };

        Assert.Throws<InvalidOperationException>(() => diagnostics.Start(FakeCaptureBackend.Ethernet.Id));
        await diagnostics.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(CaptureState.Stopped, diagnostics.Status.State);
    }
}
