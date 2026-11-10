using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Abb.Rws.Client.Internal;

namespace Abb.Rws.Client;

/// <summary>
/// 活动的 RWS WebSocket 订阅。通过 <see cref="Events"/> 读取事件。
/// </summary>
public sealed class RwsSubscription : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly IRwsProtocol _protocol;
    private readonly string? _groupPath;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<RwsEvent> _events =
        Channel.CreateUnbounded<RwsEvent>(new UnboundedChannelOptions { SingleWriter = true });
    private readonly Task _receiveTask;

    private RwsSubscription(ClientWebSocket socket, IRwsProtocol protocol, string? groupPath,
        IReadOnlyList<RwsEvent> initialEvents)
    {
        _socket = socket;
        _protocol = protocol;
        _groupPath = groupPath;
        GroupId = int.TryParse(groupPath?.Split('/').LastOrDefault(), out var groupId) ? groupId : null;
        InitialEvents = initialEvents;
        foreach (var initialEvent in initialEvents)
        {
            _events.Writer.TryWrite(initialEvent);
        }

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    /// <summary>订阅组编号，可用于运行时增删资源。</summary>
    public int? GroupId { get; }

    /// <summary>创建订阅时控制器返回的初始事件。</summary>
    public IReadOnlyList<RwsEvent> InitialEvents { get; }

    /// <summary>实时事件流。</summary>
    public ChannelReader<RwsEvent> Events => _events.Reader;

    /// <summary>WebSocket 是否仍处于打开状态。</summary>
    public bool IsConnected => _socket.State == WebSocketState.Open;

    internal static async Task<RwsSubscription> ConnectAsync(IRwsProtocol protocol, string wsUrl,
        string? groupPath, IReadOnlyList<RwsEvent> initialEvents, bool verifyCertificate,
        CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(protocol.Version == RwsProtocolVersion.Rws2
            ? "rws_subscription"
            : "robapi2_subscription");

        var cookies = protocol.Cookies.GetAllCookies();
        var cookieHeader = string.Join("; ", cookies.Cast<Cookie>().Select(c => $"{c.Name}={c.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            socket.Options.SetRequestHeader("Cookie", cookieHeader);
        }

        var authorization = protocol.CreateWebSocketAuthorization(new Uri(wsUrl));
        if (!string.IsNullOrEmpty(authorization))
        {
            socket.Options.SetRequestHeader("Authorization", authorization);
        }

        if (!verifyCertificate)
        {
            socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }

        await socket.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
        return new RwsSubscription(socket, protocol, groupPath, initialEvents);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _events.Writer.TryComplete();
                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(stream.ToArray());
                var parsed = RwsXhtmlParser.Parse(text);
                foreach (var item in parsed.Items)
                {
                    _events.Writer.TryWrite(RwsClient.ToEvent(item));
                }
            }

            _events.Writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            _events.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _events.Writer.TryComplete(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // 忽略关闭阶段的网络异常
        }

        _socket.Dispose();
        try
        {
            await _receiveTask.ConfigureAwait(false);
        }
        catch
        {
            // 接收循环内部已处理异常
        }

        if (!string.IsNullOrEmpty(_groupPath))
        {
            try
            {
                await _protocol.DeleteAsync(_groupPath, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 控制器可能已经清理订阅组
            }
        }

        _cts.Dispose();
    }
}
