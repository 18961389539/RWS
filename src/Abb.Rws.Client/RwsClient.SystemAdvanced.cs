using System.Globalization;

namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>读取控制器时钟（UTC）。</summary>
    public async Task<ControllerClockInfo> GetSystemClockAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/clock", ct).ConfigureAwait(false)).First();
        return new ControllerClockInfo(item.Value("datetime") ?? string.Empty);
    }

    /// <summary>读取控制器时区。</summary>
    public async Task<string> GetTimeZoneAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/clock/timezone", ct).ConfigureAwait(false)).First();
        return item.Value("timezone") ?? item.Value("name") ?? item.Title ?? string.Empty;
    }

    /// <summary>读取控制器时间服务器。</summary>
    public async Task<string> GetTimeServerAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("ctrl/clock/timeserver", ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault();
        return item?.Value("timeserver") ?? item?.Value("name") ?? string.Empty;
    }

    /// <summary>读取控制器网络接口配置。</summary>
    public async Task<IReadOnlyList<NetworkInterfaceInfo>> GetNetworkSettingsAsync(
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("ctrl/network", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "ctrl-netw")
            .Select(item => new NetworkInterfaceInfo(
                item.Title ?? string.Empty,
                item.Value("logical-name") ?? string.Empty,
                item.Value("network") ?? string.Empty,
                item.Value("addr") ?? string.Empty,
                item.Value("mask") ?? string.Empty,
                string.Equals(item.Value("dhcp"), "Enabled", StringComparison.OrdinalIgnoreCase),
                item.Value("gateway"))).ToList();
    }

    /// <summary>读取备份/恢复状态，如 Backup Ready、Backup in progress。</summary>
    public async Task<string> GetBackupStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/backup/state", ct).ConfigureAwait(false)).First();
        return item.Value("backup-state") ?? item.Title ?? string.Empty;
    }

    /// <summary>创建控制器备份（异步）。返回进度 Location。</summary>
    public async Task<string?> CreateBackupAsync(string backupPath, bool archive = true,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["backup"] = backupPath,
            ["archive"] = archive ? "TRUE" : "FALSE"
        };
        var result = await _protocol.PostAsync("ctrl/backup?action=backup", form, ct).ConfigureAwait(false);
        return result.Location;
    }

    /// <summary>检查备份是否可恢复。</summary>
    public async Task<string> CheckRestoreAsync(string backupPath, string ignore = "none",
        bool includeControllerSettings = true, bool includeSafetySettings = true, string include = "all",
        CancellationToken ct = default)
    {
        var query = "ctrl/backup?action=check-restore" +
                    $"&backup={Uri.EscapeDataString(backupPath)}" +
                    $"&ignore={Uri.EscapeDataString(ignore)}" +
                    $"&include-cs={(includeControllerSettings ? "true" : "false")}" +
                    $"&includess={(includeSafetySettings ? "true" : "false")}" +
                    $"&include={Uri.EscapeDataString(include)}";
        var item = (await _protocol.GetAsync(query, ct).ConfigureAwait(false)).First();
        return item.Value("status") ?? string.Empty;
    }

    /// <summary>恢复备份；成功后控制器会重启。高风险操作。</summary>
    public Task RestoreBackupAsync(string backupPath, string ignore = "none", bool deleteDirectory = true,
        bool includeControllerSettings = true, bool includeSafetySettings = true, string include = "all",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["backup"] = backupPath,
            ["ignore"] = ignore,
            ["delete-dir"] = deleteDirectory ? "true" : "false",
            ["include-cs"] = includeControllerSettings ? "true" : "false",
            ["include-ss"] = includeSafetySettings ? "true" : "false",
            ["include"] = include
        };
        return _protocol.PostAsync("ctrl/backup?action=restore", form, ct);
    }

    /// <summary>重启或关闭控制器。高风险操作；mode 可为 restart/xstart/shutdown/istart/pstart/bstart。</summary>
    public Task RestartControllerAsync(string mode = "restart", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["restart-mode"] = mode };
        return _protocol.PostAsync("ctrl", form, ct);
    }

    /// <summary>设置控制器时钟（UTC）。</summary>
    public Task SetSystemClockAsync(DateTime utc, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["sys-clock-year"] = utc.Year.ToString(CultureInfo.InvariantCulture),
            ["sys-clock-month"] = utc.Month.ToString("00", CultureInfo.InvariantCulture),
            ["sys-clock-day"] = utc.Day.ToString("00", CultureInfo.InvariantCulture),
            ["sys-clock-hour"] = utc.Hour.ToString("00", CultureInfo.InvariantCulture),
            ["sys-clock-min"] = utc.Minute.ToString("00", CultureInfo.InvariantCulture),
            ["sys-clock-sec"] = utc.Second.ToString("00", CultureInfo.InvariantCulture)
        };
        return _protocol.PutFormAsync("ctrl/clock", form, ct);
    }

    /// <summary>设置控制器时区（tz database 名称，如 Asia/Shanghai）。</summary>
    public Task SetTimeZoneAsync(string timeZone, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["timezone"] = timeZone };
        return _protocol.PostAsync("ctrl/clock/timezone?action=set-timezone", form, ct);
    }

    /// <summary>设置控制器时间服务器。</summary>
    public Task SetTimeServerAsync(string timeServer, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["timeserver"] = timeServer };
        return _protocol.PostAsync("ctrl/clock/timeserver?action=set-timeserver", form, ct);
    }

    /// <summary>设置控制器网络配置；method 为 fixip/dhcp/noip。高风险操作，需重启生效。</summary>
    public Task SetNetworkSettingsAsync(string method, string? address = null, string? mask = null,
        string? gateway = null, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["method"] = method };
        if (!string.IsNullOrEmpty(address))
        {
            form["address"] = address;
        }

        if (!string.IsNullOrEmpty(mask))
        {
            form["mask"] = mask;
        }

        if (!string.IsNullOrEmpty(gateway))
        {
            form["gateway"] = gateway;
        }

        return _protocol.PostAsync("ctrl/network?action=set", form, ct);
    }
}
