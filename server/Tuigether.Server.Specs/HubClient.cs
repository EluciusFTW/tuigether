using System.Collections.Concurrent;
using System.Text.Json;
using Alba;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Tuigether.Server.Specs;

/// <summary>
/// A SignalR client of one node. It keeps every message that the node pushes to it.
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HubConnection _connection;
    private readonly ConcurrentQueue<JsonElement> _received = new();
    private readonly TaskCompletionSource<Exception?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<ConnectRejected?> _rejected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HubClient(HubConnection connection)
    {
        _connection = connection;
        _connection.On<string>(HubProtocol.ReceiveMessage, json => _received.Enqueue(JsonDocument.Parse(json).RootElement.Clone()));
        _connection.On<ConnectRejected>(HubProtocol.ConnectRejected, rejected => _rejected.TrySetResult(rejected));
        _connection.Closed += error =>
        {
            _closed.TrySetResult(error);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// The error with which the node closed the connection, or the error of the connect.
    /// </summary>
    public Task<Exception?> Closed => _closed.Task;

    public Task<ConnectRejected?> Rejected => _rejected.Task;

    public static async Task<HubClient> ConnectAsync(IAlbaHost node, string apiKey, string protocolVersion, string userName)
    {
        var server = node.Server;

        // The test server has no real network, so the connection uses its handler. Long polling is the transport
        // that works through a plain HTTP handler.
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, HubProtocol.Path),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    options.Headers[RequestHeaders.ApiKey] = apiKey;
                    options.Headers[RequestHeaders.ProtocolVersion] = protocolVersion;
                    options.Headers[RequestHeaders.UserName] = userName;
                })
            .Build();

        var client = new HubClient(connection);
        try
        {
            await connection.StartAsync();
        }
        catch (Exception error) when (error is HubException or HttpRequestException)
        {
            client._closed.TrySetResult(error);
        }

        return client;
    }

    public Task SendAsync(object message) =>
        _connection.InvokeAsync(
            HubProtocol.ReceiveMessage,
            JsonSerializer.Serialize(new { type = message.GetType().FullName, data = message }, Json));

    /// <summary>
    /// The messages of type <typeparamref name="T"/> that the node pushed so far, in their order.
    /// </summary>
    public IReadOnlyList<T> Received<T>() =>
        _received
            .Where(static message => message.GetProperty("type").GetString() == typeof(T).FullName)
            .Select(static message => message.GetProperty("data").Deserialize<T>(Json))
            .OfType<T>()
            .ToList();

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
