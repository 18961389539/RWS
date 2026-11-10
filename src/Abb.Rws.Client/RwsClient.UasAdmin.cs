namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    // ── 角色 / 授权读取 ────────────────────────────────────────────────────

    /// <summary>列出 UAS 角色。</summary>
    public async Task<IReadOnlyList<UasRoleInfo>> ListUasRolesAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("uas/roles", ct).ConfigureAwait(false);
        return result.Items.Select(item => new UasRoleInfo(
            item.Value("rolename") ?? item.Title ?? string.Empty,
            item.Value("description"))).ToList();
    }

    /// <summary>读取角色描述。</summary>
    public async Task<string> GetUasRoleDescriptionAsync(string role, CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"uas/roles/{Escape(role)}/description", ct)
            .ConfigureAwait(false)).First();
        return item.Value("description") ?? string.Empty;
    }

    /// <summary>列出角色的 Grants。</summary>
    public async Task<IReadOnlyList<string>> GetUasRoleGrantsAsync(string role, CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"uas/roles/{Escape(role)}/grants", ct)
            .ConfigureAwait(false);
        return result.Items.Select(item => item.Value("grant") ?? item.Value("grant-name") ?? item.Title ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>列出控制器上全部 UAS Grants。</summary>
    public async Task<IReadOnlyList<UasGrantInfo>> ListAllUasGrantsAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("uas/grants", ct).ConfigureAwait(false);
        return result.Items.Select(item => new UasGrantInfo(
            item.Value("grant-name") ?? item.Title ?? string.Empty,
            item.Value("display-name"),
            item.Value("description"))).ToList();
    }

    // ── 用户读取 ──────────────────────────────────────────────────────────

    /// <summary>读取 UAS 用户详情。</summary>
    public async Task<UasUserDetails> GetUasUserAsync(string userName, CancellationToken ct = default)
    {
        var description = await GetUasUserDescriptionAsync(userName, ct).ConfigureAwait(false);
        var enabled = await GetUasUserEnabledAsync(userName, ct).ConfigureAwait(false);
        var passwordChange = await GetUasUserPasswordChangeAllowedAsync(userName, ct).ConfigureAwait(false);
        return new UasUserDetails(userName, description, enabled, passwordChange);
    }

    /// <summary>读取用户描述。</summary>
    public async Task<string> GetUasUserDescriptionAsync(string userName, CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"uas/users/{Escape(userName)}/description", ct)
            .ConfigureAwait(false)).First();
        return item.Value("description") ?? string.Empty;
    }

    /// <summary>读取用户是否启用。</summary>
    public async Task<bool> GetUasUserEnabledAsync(string userName, CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"uas/users/{Escape(userName)}/enable", ct)
            .ConfigureAwait(false)).First();
        return string.Equals(item.Value("enabled"), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>读取用户是否允许修改密码。</summary>
    public async Task<bool> GetUasUserPasswordChangeAllowedAsync(string userName,
        CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"uas/users/{Escape(userName)}/password-change-allow", ct)
            .ConfigureAwait(false)).First();
        return string.Equals(item.Value("password-change-allow"), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>列出 LDAP 相关资源名称。</summary>
    public async Task<IReadOnlyList<string>> ListLdapAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("uas/ldap", ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Title ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    // ── UAS 配置导入导出 ──────────────────────────────────────────────────

    /// <summary>下载 UAS 配置数据库（加密/不透明二进制）。</summary>
    public Task<byte[]> DownloadUasConfigurationAsync(CancellationToken ct = default)
    {
        return _protocol.GetBytesAsync("uas/configuration", "application/xhtml+xml;v=2.0", ct);
    }

    /// <summary>上传 UAS 配置数据库。极高风险，可能覆盖全部用户与授权。</summary>
    public Task UploadUasConfigurationAsync(byte[] configuration, CancellationToken ct = default)
    {
        return _protocol.PutBytesAsync("uas/configuration", configuration, ct);
    }

    // ── 用户写入 ──────────────────────────────────────────────────────────

    /// <summary>创建或更新 UAS 用户。需要 UAS 管理员权限。</summary>
    public Task CreateOrUpdateUasUserAsync(string userName, string? password = null,
        string? description = null, bool? enabled = null, bool? passwordChangeAllowed = null,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>();
        if (password is not null)
        {
            form["password"] = password;
        }

        if (description is not null)
        {
            form["description"] = description;
        }

        if (enabled is not null)
        {
            form["enabled"] = enabled.Value ? "true" : "false";
        }

        if (passwordChangeAllowed is not null)
        {
            form["password-change-allowed"] = passwordChangeAllowed.Value ? "true" : "false";
        }

        return _protocol.PostAsync($"uas/users/{Escape(userName)}", form, ct);
    }

    /// <summary>删除 UAS 用户。</summary>
    public Task DeleteUasUserAsync(string userName, CancellationToken ct = default)
    {
        return _protocol.DeleteAsync($"uas/users/{Escape(userName)}", ct);
    }

    /// <summary>启用/禁用 UAS 用户。</summary>
    public Task SetUasUserEnabledAsync(string userName, bool enabled, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["enabled"] = enabled ? "true" : "false" };
        return _protocol.PostAsync($"uas/users/{Escape(userName)}", form, ct);
    }

    /// <summary>修改 UAS 用户描述。</summary>
    public Task SetUasUserDescriptionAsync(string userName, string description,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["description"] = description };
        return _protocol.PostAsync($"uas/users/{Escape(userName)}", form, ct);
    }

    /// <summary>管理员设置 UAS 用户密码。</summary>
    public Task SetUasUserPasswordAsync(string userName, string password, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["password"] = password };
        return _protocol.PostAsync($"uas/users/{Escape(userName)}", form, ct);
    }

    /// <summary>模拟其他用户（RWS 2.0）。</summary>
    public Task ImpersonateUserAsync(string uid, CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("Impersonate 仅在 RWS 2.0 上提供");
        }

        var form = new Dictionary<string, string> { ["uid"] = uid };
        return _protocol.PostAsync("users/impersonate", form, ct);
    }

    // ── 角色写入 ──────────────────────────────────────────────────────────

    /// <summary>创建或修改角色描述。需要 UAS 管理员权限。</summary>
    public Task CreateOrUpdateUasRoleAsync(string role, string description,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["description"] = description };
        return _protocol.PostAsync($"uas/roles/{Escape(role)}", form, ct);
    }

    /// <summary>删除 UAS 角色。</summary>
    public Task DeleteUasRoleAsync(string role, CancellationToken ct = default)
    {
        return _protocol.DeleteAsync($"uas/roles/{Escape(role)}", ct);
    }

    /// <summary>修改角色描述。</summary>
    public Task SetUasRoleDescriptionAsync(string role, string description,
        CancellationToken ct = default)
    {
        return CreateOrUpdateUasRoleAsync(role, description, ct);
    }
}
