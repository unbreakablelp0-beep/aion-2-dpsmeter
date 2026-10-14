using System.Net;
using System.Runtime.InteropServices;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Capture.Npcap;

/// <summary>
/// Liest die Windows-Verbindungstabelle (wie „netstat -ano“) über iphlpapi.dll – nur lesend.
/// Derzeit nur IPv4 (TCP-Verbindungen und UDP-Endpunkte).
/// </summary>
public sealed class WindowsProcessConnectionSource : IProcessConnectionSource
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private const uint ErrorInsufficientBuffer = 122;

    /// <inheritdoc />
    public IReadOnlyList<ProcessConnection> GetConnections()
    {
        var result = new List<ProcessConnection>();
        result.AddRange(ReadTable<MibTcpRowOwnerPid>(
            (IntPtr buffer, ref int size) => GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0),
            row => new ProcessConnection(
                (int)row.OwningPid,
                TransportProtocol.Tcp,
                ToAddress(row.LocalAddr),
                ToPort(row.LocalPort),
                ToAddress(row.RemoteAddr),
                ToPort(row.RemotePort))));
        result.AddRange(ReadTable<MibUdpRowOwnerPid>(
            (IntPtr buffer, ref int size) => GetExtendedUdpTable(buffer, ref size, false, AfInet, UdpTableOwnerPid, 0),
            row => new ProcessConnection(
                (int)row.OwningPid,
                TransportProtocol.Udp,
                ToAddress(row.LocalAddr),
                ToPort(row.LocalPort))));
        return result;
    }

    private delegate uint TableReader(IntPtr buffer, ref int size);

    private static IEnumerable<ProcessConnection> ReadTable<TRow>(TableReader read, Func<TRow, ProcessConnection> map)
        where TRow : struct
    {
        var size = 0;
        read(IntPtr.Zero, ref size);

        // Die Tabelle kann zwischen den Aufrufen wachsen – dann erneut mit größerem Puffer.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = read(buffer, ref size);
                if (status == ErrorInsufficientBuffer) continue;
                if (status != 0) throw new InvalidOperationException($"Verbindungstabelle nicht lesbar (Fehler {status}).");

                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<TRow>();
                var rows = new List<ProcessConnection>(count);
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<TRow>(buffer + 4 + i * rowSize);
                    rows.Add(map(row));
                }

                return rows;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("Verbindungstabelle ändert sich zu schnell.");
    }

    private static string ToAddress(uint address) => new IPAddress(address).ToString();

    // Ports stehen in Netzwerk-Bytereihenfolge in den unteren 16 Bit.
    private static int ToPort(uint port) => ((int)(port & 0xFF) << 8) | (int)((port >> 8) & 0xFF);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);
}
