using System.Globalization;

namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>读取 DIPC 服务限制。</summary>
    public async Task<IpcInfo> GetIpcInfoAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/dipc", ct).ConfigureAwait(false)).First();
        return new IpcInfo(
            ParseLong(item.Value("max-body-size")),
            ParseLong(item.Value("max-pkg-size")));
    }

    /// <summary>列出控制器上的 DIPC 队列名称。</summary>
    public async Task<IReadOnlyList<string>> GetIpcQueuesAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/dipc", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "dipc-queue-li")
            .Select(item => item.Title ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>删除 DIPC 队列。</summary>
    public Task DeleteIpcQueueAsync(string queueName, CancellationToken ct = default)
    {
        if (Version == RwsProtocolVersion.Rws2)
        {
            return _protocol.DeleteAsync($"rw/dipc/{Escape(queueName)}", ct);
        }

        return _protocol.PostAsync($"rw/dipc/{Escape(queueName)}?action=dipc-delete", null, ct);
    }

    /// <summary>创建 DIPC 队列。</summary>
    public Task CreateIpcQueueAsync(string queueName, int queueSize = 100, int maxMessageSize = 100,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["dipc-queue-name"] = queueName,
            ["dipc-queue-size"] = queueSize.ToString(CultureInfo.InvariantCulture),
            ["dipc-max-msg-size"] = maxMessageSize.ToString(CultureInfo.InvariantCulture)
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/dipc"
            : "rw/dipc?action=dipc-create";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>读取并消费队列中的 IPC 消息。</summary>
    public async Task<IReadOnlyList<IpcMessage>> ReadIpcMessagesAsync(string queueName, int timeoutMs = 0,
        CancellationToken ct = default)
    {
        var query = timeoutMs > 0 ? $"?timeout={timeoutMs}" : string.Empty;
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/dipc/{queueName}{query}"
            : $"rw/dipc/{queueName}?action=dipc-read{(timeoutMs > 0 ? $"&timeout={timeoutMs}" : string.Empty)}";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.Items.Select(item => new IpcMessage(
            item.Value("queue-name") ?? queueName,
            item.Value("dipc-data") ?? string.Empty,
            item.Value("dipc-cmd") ?? string.Empty,
            item.Value("dipc-userdef") ?? string.Empty,
            item.Value("dipc-msgtype") ?? string.Empty)).ToList();
    }

    /// <summary>向目标队列发送 IPC 消息。</summary>
    public Task SendIpcMessageAsync(string targetQueue, string data, string sourceQueue, int command = 111,
        int userDef = 0, int messageType = 1, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["dipc-src-queue-name"] = sourceQueue,
            ["dipc-cmd"] = command.ToString(CultureInfo.InvariantCulture),
            ["dipc-userdef"] = userDef.ToString(CultureInfo.InvariantCulture),
            ["dipc-msgtype"] = messageType.ToString(CultureInfo.InvariantCulture),
            ["dipc-data"] = data
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/dipc/{targetQueue}"
            : $"rw/dipc/{targetQueue}?action=dipc-send";
        return _protocol.PostAsync(resource, form, ct);
    }
}
