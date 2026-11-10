namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>读取设备树根分组（HW_DEVICES / SW_RESOURCES）。</summary>
    public Task<RwsDeviceInfo> GetDeviceGroupsAsync(CancellationToken ct = default) =>
        GetDeviceAsync(string.Empty, ct);

    /// <summary>
    /// 读取一个设备节点。path 形如 HW_DEVICES/MAIN_COMPUTER，空字符串表示设备树根。
    /// 返回该节点的子设备和属性。
    /// </summary>
    public async Task<RwsDeviceInfo> GetDeviceAsync(string path, CancellationToken ct = default)
    {
        var normalized = path.Trim('/');
        var resource = normalized.Length == 0 ? "rw/devices" : $"rw/devices/{EscapePath(normalized)}";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);

        var children = new List<RwsDeviceInfo>();
        var properties = new List<RwsDeviceProperty>();
        foreach (var item in result.Items)
        {
            var type = item.Type ?? string.Empty;
            if (type.StartsWith("dev-id", StringComparison.OrdinalIgnoreCase))
            {
                var id = item.Title ?? string.Empty;
                var childPath = normalized.Length == 0 ? id : $"{normalized}/{id}";
                children.Add(new RwsDeviceInfo(id, item.Value("name") ?? id, childPath,
                    [], []));
            }
            else if (type.StartsWith("dev-prop", StringComparison.OrdinalIgnoreCase))
            {
                properties.Add(new RwsDeviceProperty(
                    item.Title ?? string.Empty,
                    item.Value("name") ?? item.Title ?? string.Empty,
                    item.Value("value") ?? string.Empty,
                    item.Value("unit")));
            }
        }

        var nodeId = normalized.Length == 0
            ? "devices"
            : normalized[(normalized.LastIndexOf('/') + 1)..];
        return new RwsDeviceInfo(nodeId, nodeId, normalized, children, properties);
    }

    /// <summary>读取设备节点的单个属性。</summary>
    public async Task<RwsDeviceProperty?> GetDevicePropertyAsync(string path, string propertyId,
        CancellationToken ct = default)
    {
        var device = await GetDeviceAsync(path, ct).ConfigureAwait(false);
        return device.Properties.FirstOrDefault(property =>
            property.Id.Equals(propertyId, StringComparison.OrdinalIgnoreCase));
    }
}
