using System.Net;
using System.Net.Sockets;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     A local TCP server that accepts every connection and never sends a byte (AB#9388): the shape of the GitHub
///     Pages connection that stayed ESTABLISHED without response headers and hung a CK build. Optionally it answers
///     chosen paths with a 200 and stays silent for all others.
/// </summary>
internal sealed class SilentTcpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _clients = [];
    private readonly Func<string, string?>? _answer;
    private int _acceptedConnections;

    /// <param name="answer">Returns the body for a request path it answers, null to stay silent.</param>
    public SilentTcpServer(Func<string, string?>? answer = null)
    {
        _answer = answer;
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
                if (_answer != null)
                {
                    _ = AnswerAsync(client);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
            var requestLine = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
            var path = requestLine?.Split(' ').ElementAtOrDefault(1) ?? "";
            var body = _answer!(path);
            if (body == null)
            {
                return; // silent: the connection stays open without a byte
            }

            var payload = System.Text.Encoding.UTF8.GetBytes(body);
            var head = System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, _stop.Token).ConfigureAwait(false);
            await stream.WriteAsync(payload, _stop.Token).ConfigureAwait(false);
            await stream.FlushAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
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
