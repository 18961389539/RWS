namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>列出事件日志域及其事件数/缓冲区大小。</summary>
    public async Task<IReadOnlyList<ElogDomainInfo>> GetEventLogDomainsAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/elog", ct).ConfigureAwait(false);
        return result.Items.Select(item => new ElogDomainInfo(
            ParseInt(item.Title),
            ParseLong(item.Value("numevts")),
            ParseLong(item.Value("buffsize")))).ToList();
    }

    /// <summary>按全局序号读取事件日志消息。</summary>
    public async Task<AbbEventLogEntry> GetEventLogBySequenceNumberAsync(long sequenceNumber,
        CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"rw/elog/seqnum/{sequenceNumber}?lang=en", ct)
            .ConfigureAwait(false)).First();
        return ParseEventLog(item);
    }

    /// <summary>把原始事件日志保存到控制器文件系统。path 必须是 fileservice URI。</summary>
    public async Task<string?> SaveEventLogRawAsync(string path, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["path"] = path };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/elog/saveraw"
            : "rw/elog?action=saveraw";
        var result = await _protocol.PostAsync(resource, form, ct).ConfigureAwait(false);
        return result.Location;
    }
}
