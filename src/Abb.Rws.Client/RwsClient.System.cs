using System.Globalization;

namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>读取控制器系统信息（名称、RobotWare 版本等）。</summary>
    public async Task<RwsSystemInfo> GetSystemInfoAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/system", ct).ConfigureAwait(false)).First();
        return new RwsSystemInfo(
            item.Value("name") ?? string.Empty,
            (int)ParseDoubleOrZero(item.Value("major")),
            (int)ParseDoubleOrZero(item.Value("minor")),
            (int)ParseDoubleOrZero(item.Value("build")),
            (int)ParseDoubleOrZero(item.Value("revision")),
            item.Value("title") ?? string.Empty,
            item.Value("type") ?? string.Empty,
            item.Value("description") ?? string.Empty,
            item.Value("date") ?? string.Empty,
            ParseInt(item.Value("sub-revision")),
            item.Value("buildtag"),
            ParseInt(item.Value("robapi-compatibility-revision")),
            item.Value("rwversion"),
            item.Value("sysid"),
            item.Value("starttm"),
            item.Value("rwversionname"),
            item.Value("rwdistributionversion"));
    }

    /// <summary>读取机器人型号。</summary>
    public async Task<string> GetRobotTypeAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/system/robottype", ct).ConfigureAwait(false)).First();
        return item.Value("robot-type") ?? item.Title ?? string.Empty;
    }

    /// <summary>读取已安装的 RobotWare 选项。</summary>
    public async Task<IReadOnlyList<string>> GetOptionsAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/system/options", ct).ConfigureAwait(false);
        return result.Items.Select(i => i.Value("option") ?? i.Title ?? string.Empty).ToList();
    }

    /// <summary>读取许可证编号。</summary>
    public async Task<string> GetLicenseAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/system/license", ct).ConfigureAwait(false)).First();
        return item.Value("license") ?? item.Title ?? string.Empty;
    }

    private static double ParseDoubleOrZero(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
