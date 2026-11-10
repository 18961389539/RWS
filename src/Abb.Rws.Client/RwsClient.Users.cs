namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>读取当前会话的 RMMP（远程手动模式许可）状态。</summary>
    public async Task<RmmpState> GetRmmpStateAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("users/rmmp", ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Type == "user-rmmp") ?? result.First();
        return new RmmpState(
            item.Value("userid") ?? string.Empty,
            item.Value("privilege") ?? "none",
            IsTrue(item.Value("rmmpheldbyme")));
    }

    /// <summary>申请 RMMP 权限，例如 modify / exec。</summary>
    public Task RequestRmmpAsync(string privilege = "modify", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["privilege"] = privilege };
        return _protocol.PostAsync("users/rmmp", form, ct);
    }

    /// <summary>轮询 RMMP 申请状态。</summary>
    public async Task<IReadOnlyDictionary<string, string>> PollRmmpAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("users/rmmp/poll", ct).ConfigureAwait(false);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (result.Items.Count > 0)
        {
            foreach (var pair in result.First().Values)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }

    /// <summary>取消 RMMP 申请或释放已获得的权限。</summary>
    public Task CancelRmmpAsync(CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? "users/rmmp/cancel"
            : "users/rmmp?action=cancel";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>登出当前 RWS 会话。</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (Version == RwsProtocolVersion.Rws2)
        {
            await _protocol.GetAsync("logout", ct).ConfigureAwait(false);
            return;
        }

        await _protocol.GetBytesAsync("logout", "*/*", ct).ConfigureAwait(false);
    }

    /// <summary>读取当前会话登录信息。</summary>
    public async Task<RwsLoginInfo> GetLoginInfoAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("users/login-info", ct).ConfigureAwait(false)).First();
        return new RwsLoginInfo(
            item.Value("user-name") ?? string.Empty,
            item.Value("user-locale") ?? string.Empty,
            item.Value("user-application") ?? string.Empty,
            item.Value("uas-id") ?? string.Empty);
    }

    /// <summary>列出当前用户拥有的授权。</summary>
    public async Task<IReadOnlyList<string>> GetUserGrantsAsync(CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2 ? "uas/user/grants" : "users/grants";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Value("grantname") ?? item.Title ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>列出 UAS 用户（RWS 2.0，需要管理员权限）。</summary>
    public async Task<IReadOnlyList<string>> ListUasUsersAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("uas/users", ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Value("name") ?? item.Title ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>列出指定 UAS 用户的角色。</summary>
    public async Task<IReadOnlyList<string>> GetUasUserRolesAsync(string userName,
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"uas/users/{userName}/roles", ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Value("rolename") ?? item.Title ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>列出指定 UAS 用户的授权。</summary>
    public async Task<IReadOnlyList<string>> GetUasUserGrantsAsync(string userName,
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"uas/users/{userName}/grants", ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Value("grantname") ?? item.Title ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>注册 UAS 用户。需要用户管理权限，未在真机验证。</summary>
    public Task RegisterUserAsync(string username, string application, string location = "",
        string userLocale = "remote", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["application"] = application,
            ["username"] = username,
            ["location"] = location,
            ["ulocale"] = userLocale
        };
        var resource = Version == RwsProtocolVersion.Rws2 ? "users/register" : "users";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>修改当前用户密码（RWS 2.0）。未在真机验证。</summary>
    public Task ChangePasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("RWS 1.0 不提供密码修改接口");
        }

        var form = new Dictionary<string, string>
        {
            ["old-password"] = oldPassword,
            ["new-password"] = newPassword
        };
        return _protocol.PostAsync("uas/user/password", form, ct);
    }
}
