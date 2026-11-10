namespace Abb.Rws.Client.Internal;

internal sealed class RwsResult
{
    public string? SelfHref { get; set; }

    public string? NextHref { get; set; }

    public string? Location { get; set; }

    public string? RawBody { get; set; }

    public List<RwsItem> Items { get; } = [];

    public RwsItem First() => Items.Count > 0 ? Items[0] : throw new RwsException("RWS 响应中没有数据项");
}

internal sealed record RwsOptionsResult(IReadOnlyList<string> Allow, string? RawBody);

internal sealed class RwsItem
{
    public string? Title { get; set; }

    public string? Type { get; set; }

    public string? Href { get; set; }

    /// <summary>订阅组列表里 rel="events" 的 WebSocket 地址。</summary>
    public string? EventsHref { get; set; }

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Value(string key) => Values.TryGetValue(key, out var value) ? value : null;

    public string Require(string key) => Value(key) ?? throw new RwsException($"RWS 响应缺少字段 '{key}'");
}
