using System.Net;
using System.Net.Sockets;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     A local TCP server that accepts every connection and never sends a byte (AB#9388): the shape of the GitHub
///     Pages connection that stayed ESTABLISHED without response headers and hung a CK build.
/// </summary>
internal sealed class SilentTcpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _clients = [];
    private int _acceptedConnections;

    public SilentTcpServer()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public Uri BaseUri => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/catalog");

    /// <summary>Connections the server accepted (and never answered).</summary>
    public int AcceptedConnections => Volatile.Read(ref _acceptedConnections);

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                lock (_clients)
                {
                    _clients.Add(client);
                }

                Interlocked.Increment(ref _acceptedConnections);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        lock (_clients)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }

        _stop.Dispose();
    }
}
