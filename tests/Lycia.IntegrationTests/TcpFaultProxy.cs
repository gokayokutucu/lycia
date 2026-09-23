// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Net;
using System.Net.Sockets;

namespace Lycia.IntegrationTests;

/// <summary>
/// A TCP proxy in front of a broker that can misbehave on demand. Its purpose is the case a healthy broker
/// cannot produce: the broker RECEIVES a publish, but the client never observes the broker's reply, and the
/// connection is then lost. That is exactly the window in which a publisher cannot know whether its message
/// was accepted.
/// </summary>
public sealed class TcpFaultProxy : IAsyncDisposable, IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _sockets = [];
    private readonly object _gate = new();
    private volatile bool _dropServerToClient;
    private int _disposed;
    private Task? _acceptLoop;

    public TcpFaultProxy(string targetHost, int targetPort)
    {
        _targetHost = targetHost;
        _targetPort = targetPort;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string ConnectionString => $"amqp://guest:guest@127.0.0.1:{Port}";

    public TcpFaultProxy Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        return this;
    }

    /// <summary>
    /// While true, everything the broker sends to the client is read and discarded. The broker still receives
    /// and acts on everything the client sends, so it accepts publishes whose confirms the client never sees.
    /// </summary>
    public void DropServerToClient(bool drop) => _dropServerToClient = drop;

    /// <summary>Closes every proxied connection, as a network failure would.</summary>
    public void SeverConnections()
    {
        lock (_gate)
        {
            foreach (var socket in _sockets)
                try { socket.Client.Close(0); } catch { /* already closed */ }
            _sockets.Clear();
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch { return; }

            var upstream = new TcpClient();
            try { await upstream.ConnectAsync(_targetHost, _targetPort).ConfigureAwait(false); }
            catch { client.Dispose(); continue; }

            lock (_gate) { _sockets.Add(client); _sockets.Add(upstream); }
            _ = Task.Run(() => PumpAsync(client, upstream, serverToClient: false));
            _ = Task.Run(() => PumpAsync(upstream, client, serverToClient: true));
        }
    }

    private async Task PumpAsync(TcpClient from, TcpClient to, bool serverToClient)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            var source = from.GetStream();
            var destination = to.GetStream();
            while (!_stop.IsCancellationRequested)
            {
                var read = await source.ReadAsync(buffer, 0, buffer.Length, _stop.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (serverToClient && _dropServerToClient) continue;
                await destination.WriteAsync(buffer, 0, read, _stop.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // a severed or closed socket ends the pump
        }
        finally
        {
            try { from.Client.Close(0); } catch { /* already closed */ }
            try { to.Client.Close(0); } catch { /* already closed */ }
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _stop.Cancel();
        _listener.Stop();
        SeverConnections();
        if (_acceptLoop != null) try { await _acceptLoop.ConfigureAwait(false); } catch { /* stopping */ }
        _stop.Dispose();
    }
}
