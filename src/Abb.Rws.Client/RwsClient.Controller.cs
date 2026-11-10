namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    // ── 控制器身份 ────────────────────────────────────────────────────────

    /// <summary>读取控制器身份（名称、ID、类型、MAC）。</summary>
    public async Task<ControllerIdentity> GetControllerIdentityAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/identity", ct).ConfigureAwait(false)).First();
        return new ControllerIdentity(
            item.Value("ctrl-name") ?? string.Empty,
            item.Value("ctrl-id") ?? string.Empty,
            item.Value("ctrl-type") ?? string.Empty,
            item.Value("ctrl-mac") ?? string.Empty);
    }

    /// <summary>修改控制器名称。</summary>
    public Task SetControllerNameAsync(string name, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["ctrl-name"] = name };
        return _protocol.PutFormAsync("ctrl/identity", form, ct);
    }

    // ── 证书存储 ──────────────────────────────────────────────────────────

    /// <summary>列出证书存储区（controller / system）。</summary>
    public async Task<IReadOnlyList<string>> GetCertificateStoresAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("ctrl/certstore", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "ctrl-certstore-li")
            .Select(item => item.Value("store-name") ?? item.Title ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>读取指定证书存储区中的 PEM 证书。storePath 形如 controller/trust_ca_store。</summary>
    public async Task<IReadOnlyList<string>> GetCertificatesAsync(string storePath,
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"ctrl/certstore/{EscapePath(storePath)}", ct)
            .ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "ctrl-certstore-cert")
            .Select(item => item.Value("cert-pem") ?? string.Empty)
            .Where(pem => pem.Length > 0)
            .ToList();
    }

    // ── 安全状态（只读） ──────────────────────────────────────────────────

    /// <summary>读取安全模式状态。</summary>
    public async Task<SafetyModeInfo> GetSafetyModeAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/safety/mode", ct).ConfigureAwait(false)).First();
        return new SafetyModeInfo(
            ParseInt(item.Value("userdata")),
            item.Value("safetymode") ?? string.Empty);
    }

    /// <summary>读取安全制动器检查状态。</summary>
    public async Task<SafetyBrakeCheckInfo> GetSafetyBrakeCheckAsync(int driveNumber = 1,
        CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"ctrl/safety/cbc?drivenum={driveNumber}", ct)
            .ConfigureAwait(false)).First();
        return new SafetyBrakeCheckInfo(
            item.Value("next-brake-check-time") ?? string.Empty,
            item.Value("last-brake-check-status") ?? string.Empty,
            item.Value("status") ?? string.Empty);
    }

    /// <summary>读取安全控制器负载状态。</summary>
    public async Task<string> GetSafetyLoadStatusAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/safety/load", ct).ConfigureAwait(false)).First();
        return item.Value("status") ?? string.Empty;
    }

    /// <summary>读取安全配置状态。</summary>
    public async Task<SafetyConfigurationInfo> GetSafetyConfigurationAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("ctrl/safety/config", ct).ConfigureAwait(false);
        var status = result.Items.FirstOrDefault(item => item.Value("config-status") is not null)
            ?.Value("config-status") ?? string.Empty;
        var config = result.Items.FirstOrDefault(item => item.Value("sw-major-ver") is not null);
        return new SafetyConfigurationInfo(
            status,
            FormatVersion(config?.Values, "sw-major-ver", "sw-minor-ver", "sw-rev"),
            FormatVersion(config?.Values, "file-major-ver", "file-minor-ver", "file-rev"),
            config?.Value("creation-date") ?? string.Empty,
            config?.Value("created-by") ?? string.Empty,
            config?.Value("config-name") ?? string.Empty,
            config?.Value("checksum") ?? string.Empty);
    }

    /// <summary>读取安全违规信息。</summary>
    public async Task<SafetyViolationInfo> GetSafetyViolationAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/safety/violation", ct).ConfigureAwait(false)).First();
        return new SafetyViolationInfo(
            new Dictionary<string, string>(item.Values, StringComparer.OrdinalIgnoreCase));
    }

    // ── 控制器注册表 / 选项 / 特性 ────────────────────────────────────────

    /// <summary>列出控制器注册表条目。</summary>
    public async Task<IReadOnlyList<string>> GetRegistryEntriesAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("ctrl/registry", ct).ConfigureAwait(false);
        return result.Items
            .Select(item => item.Title ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>读取注册表条目的原始 XML 内容。</summary>
    public async Task<string> GetRegistryContentAsync(string entry, CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"ctrl/registry/{Escape(entry)}", ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(entryItem => entryItem.Type == "ctrl-reg-file-li")
                   ?? result.First();
        return item.Values.Values.FirstOrDefault(value => value.Length > 0) ?? string.Empty;
    }

    /// <summary>列出控制器选项。</summary>
    public Task<IReadOnlyList<string>> GetControllerOptionsAsync(CancellationToken ct = default) =>
        GetNameListAsync("ctrl/options", ct);

    /// <summary>列出控制器特性。</summary>
    public Task<IReadOnlyList<string>> GetControllerFeaturesAsync(CancellationToken ct = default) =>
        GetNameListAsync("ctrl/features", ct);

    // ── 文件压缩 ──────────────────────────────────────────────────────────

    /// <summary>在控制器文件系统中压缩文件或目录。</summary>
    public Task CompressAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["srcpath"] = sourcePath,
            ["dstpath"] = destinationPath
        };
        return _protocol.PostAsync("ctrl/compress", form, ct);
    }

    private async Task<IReadOnlyList<string>> GetNameListAsync(string resource, CancellationToken ct)
    {
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.Items
            .Select(item => item.Title ?? item.Value("name") ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
    }

    private static string FormatVersion(IReadOnlyDictionary<string, string>? values, string major,
        string minor, string revision)
    {
        if (values is null)
        {
            return string.Empty;
        }

        values.TryGetValue(major, out var majorValue);
        values.TryGetValue(minor, out var minorValue);
        values.TryGetValue(revision, out var revisionValue);
        return $"{majorValue ?? string.Empty}.{minorValue ?? string.Empty}.{revisionValue ?? string.Empty}";
    }
}
