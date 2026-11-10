namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>列出 IO 网络。</summary>
    public async Task<IReadOnlyList<IoNetworkInfo>> GetIoNetworksAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/iosystem/networks", ct).ConfigureAwait(false);
        return result.Items.Select(item => new IoNetworkInfo(
            item.Value("name") ?? item.Title ?? string.Empty,
            item.Value("lstate"),
            item.Value("pstate"))).ToList();
    }

    /// <summary>列出所有 IO 设备。</summary>
    public async Task<IReadOnlyList<IoDeviceInfo>> GetIoDevicesAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/iosystem/devices", ct).ConfigureAwait(false);
        return result.Items.Select(ParseIoDevice).ToList();
    }

    /// <summary>读取单个 IO 设备。</summary>
    public async Task<IoDeviceInfo> GetIoDeviceAsync(string network, string device,
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}"
            : $"rw/iosystem/devices/{Escape(device)}";
        var item = (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First();
        return ParseIoDevice(item);
    }

    /// <summary>设置信号仿真状态。需要 Edit 控制权。</summary>
    public Task SetSignalSimulatedAsync(string signal, bool simulated, string network = "Local",
        string device = "DRV_1", CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/signals/{Escape(network)}/{Escape(device)}/{Escape(signal)}/set-lstate"
            : $"rw/iosystem/signals/{Escape(network)}/{Escape(device)}/{Escape(signal)}?action=set";
        var form = new Dictionary<string, string> { ["lstate"] = simulated ? "simulated" : "not simulated" };
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>解除所有信号的阻塞/仿真。</summary>
    public Task UnblockAllSignalsAsync(CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/iosystem/signals/unblock-signal"
            : "rw/iosystem/signals?action=unblock-signal";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>设置 IO 网络状态（RWS 2.0 专用）。</summary>
    public Task SetIoNetworkStateAsync(string network, bool enabled, CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("IO 网络状态写入仅在 RWS 2.0 上受支持");
        }

        var form = new Dictionary<string, string> { ["lstate"] = enabled ? "enabled" : "disabled" };
        return _protocol.PostAsync($"rw/iosystem/networks/{Escape(network)}/set-lstate", form, ct);
    }

    private static IoDeviceInfo ParseIoDevice(Internal.RwsItem item)
    {
        var title = item.Title ?? string.Empty;
        var slash = title.IndexOf('/');
        var network = slash > 0 ? title[..slash] : string.Empty;
        return new IoDeviceInfo(
            item.Value("name") ?? (slash > 0 ? title[(slash + 1)..] : title),
            network,
            item.Value("lstate"),
            item.Value("pstate"),
            item.Value("address"));
    }
}
