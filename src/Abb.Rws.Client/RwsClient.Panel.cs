namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>设置电机上下电。需要 Edit 控制权。</summary>
    public Task SetControllerStateAsync(bool motorOn, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["ctrl-state"] = motorOn ? "motoron" : "motoroff" };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/panel/ctrl-state"
            : "rw/panel/ctrlstate?action=setctrlstate";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>读取事件日志单条详情。</summary>
    public async Task<AbbEventLogEntry> GetEventLogMessageAsync(int domain, long sequenceNumber,
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/elog/{domain}/{sequenceNumber}?lang=en"
            : $"rw/elog/{domain}/{sequenceNumber}?lang=en";
        var item = (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First();
        return new AbbEventLogEntry(
            sequenceNumber,
            ParseInt(item.Value("msgtype")),
            ParseLong(item.Value("code")),
            item.Value("tstamp") ?? string.Empty,
            item.Value("title") ?? string.Empty,
            item.Value("desc") ?? string.Empty,
            item.Value("conseqs") ?? string.Empty,
            item.Value("causes") ?? string.Empty,
            item.Value("actions") ?? string.Empty);
    }

    /// <summary>清空一个事件日志域。</summary>
    public Task ClearEventLogAsync(int domain = 0, CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/elog/{domain}/clear"
            : $"rw/elog/{domain}?action=clear";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>清空全部事件日志域。</summary>
    public Task ClearAllEventLogsAsync(CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/elog/clearall"
            : "rw/elog?action=clearall";
        return _protocol.PostAsync(resource, null, ct);
    }
}
