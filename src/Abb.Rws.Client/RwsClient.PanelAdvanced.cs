namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>读取操作模式钥匙开关锁定状态。</summary>
    public async Task<string> GetOperationModeLockStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/panel/opmode/lock-state", ct).ConfigureAwait(false)).First();
        return item.Value("lockstate") ?? string.Empty;
    }

    /// <summary>切换操作模式（仅虚拟控制器支持）。</summary>
    public Task SetOperationModeAsync(string opmode, CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("RWS 1.0 不提供操作模式切换");
        }

        var form = new Dictionary<string, string> { ["opmode"] = opmode };
        return _protocol.PostAsync("rw/panel/opmode", form, ct);
    }

    /// <summary>确认挂起的操作模式切换。通常由人在示教器确认。</summary>
    public Task AcknowledgeOperationModeAsync(string opmode, CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("RWS 1.0 不提供操作模式确认");
        }

        var form = new Dictionary<string, string> { ["opmode"] = opmode };
        return _protocol.PostAsync("rw/panel/opmode/acknowledge", form, ct);
    }

    /// <summary>用 PIN 锁定操作模式切换。高风险，可能锁住模式切换。</summary>
    public Task LockOperationModeAsync(string pin, bool permanent = true, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["pin"] = pin,
            ["permanent"] = permanent ? "1" : "0"
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/panel/opmode/lock"
            : "rw/panel/opmode?action=lock";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>用 PIN 解锁操作模式切换。</summary>
    public Task UnlockOperationModeAsync(string pin, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["pin"] = pin };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/panel/opmode/unlock"
            : "rw/panel/opmode?action=unlock";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>读取碰撞检测状态。</summary>
    public async Task<string> GetCollisionDetectionStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/panel/coldetstate", ct).ConfigureAwait(false)).First();
        return item.Value("coldetstate") ?? string.Empty;
    }

    /// <summary>读取使能请求状态。</summary>
    public async Task<string> GetEnableRequestStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/panel/enreq", ct).ConfigureAwait(false)).First();
        return item.Value("enreq") ?? string.Empty;
    }

    /// <summary>设置面板语言。该资源只支持写入，无法读回。</summary>
    public Task SetPanelLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["lang-code"] = languageCode };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/panel/lang"
            : "rw/panel?action=setlang";
        return _protocol.PostAsync(resource, form, ct);
    }
}
