namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>
    /// 最近一次请求使用的 RWS 内容版本，如 2.0 / 2.1。RWS 1.0 返回 null。
    /// </summary>
    public string? ApiVersion => _protocol.ApiVersion;

    /// <summary>列出控制器 RWS 根资源下可用的服务。</summary>
    public async Task<IReadOnlyList<RwsServiceInfo>> GetRwsServicesAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "rwservice-li")
            .Select(item => new RwsServiceInfo(item.Title ?? string.Empty, item.Href ?? string.Empty))
            .ToList();
    }

    /// <summary>读取控制器安装的产品及版本。</summary>
    public async Task<IReadOnlyList<RwsProductVersion>> GetSystemProductsAsync(
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/system/products", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "sys-product-li")
            .Select(item => new RwsProductVersion(
                item.Title ?? string.Empty,
                item.Value("version") ?? string.Empty,
                item.Value("version-name") ?? string.Empty))
            .ToList();
    }

    /// <summary>读取 RWS 返回码说明；code 为空时返回全部返回码。</summary>
    public async Task<IReadOnlyList<RwsReturnCode>> GetReturnCodesAsync(long? code = null,
        CancellationToken ct = default)
    {
        var resource = code is null ? "rw/retcode" : $"rw/retcode?code={code.Value}";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type is "err-desc" or "err-desc-li")
            .Select(item => new RwsReturnCode(
                item.Value("name") ?? item.Title ?? string.Empty,
                ParseLong(item.Value("code")),
                item.Value("severity") ?? string.Empty,
                item.Value("description") ?? string.Empty))
            .ToList();
    }

    /// <summary>读取单个 RWS 返回码的说明。</summary>
    public async Task<RwsReturnCode?> GetReturnCodeAsync(long code, CancellationToken ct = default)
    {
        var codes = await GetReturnCodesAsync(code, ct).ConfigureAwait(false);
        return codes.FirstOrDefault();
    }

    /// <summary>查询资源支持的 HTTP 方法（OPTIONS）。</summary>
    public async Task<RwsResourceOptions> GetResourceOptionsAsync(string resource,
        CancellationToken ct = default)
    {
        var result = await _protocol.OptionsAsync(resource, ct).ConfigureAwait(false);
        return new RwsResourceOptions(result.Allow, result.RawBody);
    }
}
