using System.Net;
using System.Net.Sockets;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Liest die echte Windows-Verbindungstabelle. Die Tests öffnen nur lokale Sockets auf 127.0.0.1
/// und senden nichts.
/// </summary>
public class WindowsProcessConnectionSourceTests
{
    private static readonly int OwnPid = Environment.ProcessId;

    [Fact]
    public void GetConnections_FindsOwnTcpListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var connections = new WindowsProcessConnectionSource().GetConnections();

            Assert.Contains(connections, c =>
                c.ProcessId == OwnPid && c.Protocol == TransportProtocol.Tcp
                && c.LocalPort == port && c.LocalAddress == "127.0.0.1");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void GetConnections_FindsOwnUdpEndpoint()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;

        var connections = new WindowsProcessConnectionSource().GetConnections();

        Assert.Contains(connections, c =>
            c.ProcessId == OwnPid && c.Protocol == TransportProtocol.Udp && c.LocalPort == port);
    }

    [Fact]
    public void Filter_ForOwnConnections_ContainsTheirPorts()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;

        var own = new WindowsProcessConnectionSource().GetConnections().Where(c => c.ProcessId == OwnPid);

        Assert.Contains($"(udp port {port})", ProcessConnectionFilter.BuildFilter(own));
    }
}
