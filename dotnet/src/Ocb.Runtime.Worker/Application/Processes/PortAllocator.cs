using System.Net;
using System.Net.Sockets;

namespace Ocb.Runtime.Worker.Application.Processes;

/// <summary>
/// Allocates and tracks ephemeral TCP ports for worker processes.
/// Thread-safe; uses OS-assigned random port via binding to port 0.
/// </summary>
public sealed class PortAllocator
{
    private readonly object _lock = new();
    private readonly HashSet<int> _allocated = new();
    private readonly int _rangeStart;
    private readonly int _rangeEnd;

    public PortAllocator(int rangeStart = 30000, int rangeEnd = 31000)
    {
        _rangeStart = rangeStart;
        _rangeEnd = rangeEnd;
    }

    /// <summary>
    /// Allocates a port by binding to an OS-assigned port and
    /// recording it as in-use. Returns 0 if no port is available.
    /// </summary>
    public int Allocate()
    {
        lock (_lock)
        {
            // Try OS-assigned first, then fall back to range scanning
            var port = TryBindOsAssigned();
            if (port > 0 && _allocated.Add(port))
                return port;

            // Scan the configured range
            for (var p = _rangeStart; p <= _rangeEnd; p++)
            {
                if (_allocated.Contains(p))
                    continue;

                if (IsPortAvailable(p))
                {
                    _allocated.Add(p);
                    return p;
                }
            }

            return 0;
        }
    }

    /// <summary>
    /// Releases a previously allocated port so it can be reused.
    /// </summary>
    public void Release(int port)
    {
        lock (_lock)
        {
            _allocated.Remove(port);
        }
    }

    /// <summary>
    /// Number of currently allocated ports.
    /// </summary>
    public int AllocatedCount
    {
        get { lock (_lock) return _allocated.Count; }
    }

    private static int TryBindOsAssigned()
    {
        try
        {
            using var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            if (socket.LocalEndPoint is IPEndPoint ep)
                return ep.Port;

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
