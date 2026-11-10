namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>按信号名读取信号（虚拟信号/无 network、device 的信号）。</summary>
    public async Task<AbbSignal> GetSignalByNameAsync(string signal, CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"rw/iosystem/signals/{Escape(signal)}", ct)
            .ConfigureAwait(false)).First();
        return ParseSignal(item);
    }

    /// <summary>读取单个 IO 网络。</summary>
    public async Task<IoNetworkInfo> GetIoNetworkAsync(string network, CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"rw/iosystem/networks/{Escape(network)}", ct)
            .ConfigureAwait(false)).First();
        return new IoNetworkInfo(
            item.Value("name") ?? item.Title ?? network,
            item.Value("lstate"),
            item.Value("pstate"));
    }

    /// <summary>读取 IO 网络配置。</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetIoNetworkConfigAsync(string network,
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/networks/{Escape(network)}/config"
            : $"rw/iosystem/networks/{Escape(network)}?resource=config";
        return await GetConfigDictionaryAsync(resource, ct).ConfigureAwait(false);
    }

    /// <summary>读取 IO 设备配置。</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetIoDeviceConfigAsync(string network, string device,
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}/config"
            : $"rw/iosystem/devices/{Escape(device)}?resource=config";
        return await GetConfigDictionaryAsync(resource, ct).ConfigureAwait(false);
    }

    /// <summary>读取单个 IO 信号的配置。</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetSignalConfigAsync(string signal,
        string network = "Local", string device = "DRV_1", CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/signals/{Escape(network)}/{Escape(device)}/{Escape(signal)}/config"
            : $"rw/iosystem/signals/{Escape(network)}/{Escape(device)}/{Escape(signal)}?resource=config";
        return await GetConfigDictionaryAsync(resource, ct).ConfigureAwait(false);
    }

    /// <summary>按条件搜索 IO 信号；name 为前缀匹配。</summary>
    public async Task<IReadOnlyList<AbbSignal>> SearchSignalsAsync(string? name = null, string? device = null,
        string? network = null, string? category = null, string? type = null, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>();
        AddIfNotEmpty(form, "name", name);
        AddIfNotEmpty(form, "device", device);
        AddIfNotEmpty(form, "network", network);
        AddIfNotEmpty(form, "category", category);
        AddIfNotEmpty(form, "type", type);

        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/iosystem/signals/signal-search"
            : "rw/iosystem/signals?action=signal-search";
        var result = await _protocol.PostAsync(resource, form, ct).ConfigureAwait(false);
        return result.Items.Select(ParseSignal).ToList();
    }

    /// <summary>按名称/状态/网络搜索 IO 设备；name 和 lstate 至少填一个。</summary>
    public async Task<IReadOnlyList<IoDeviceInfo>> SearchIoDevicesAsync(string? name = null,
        string? logicalState = null, string? network = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(logicalState))
        {
            throw new ArgumentException("name 和 logicalState 至少需要提供一个");
        }

        var form = new Dictionary<string, string>();
        AddIfNotEmpty(form, "name", name);
        AddIfNotEmpty(form, "lstate", logicalState);
        AddIfNotEmpty(form, "network", network);

        var result = await _protocol.PostAsync("rw/iosystem/devices?action=search", form, ct)
            .ConfigureAwait(false);
        return result.Items.Select(ParseIoDevice).ToList();
    }

    /// <summary>启用/停用 IO 设备。</summary>
    public Task SetIoDeviceStateAsync(string network, string device, bool enabled,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["lstate"] = enabled ? "enable" : "disable" };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}"
            : $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}?action=set";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>写 IO 设备输入数据。startByte 为起始字节，signalData/dataMask 为十六进制字符串。</summary>
    public Task SetIoDeviceInputDataAsync(string network, string device, int startByte, string signalData,
        string dataMask, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["startbyte"] = startByte.ToString(),
            ["signaldata"] = signalData,
            ["datamask"] = dataMask
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}/set-inputdata"
            : $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}?action=set-inputdata";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>写 IO 设备输出数据。startByte 为起始字节，signalData/dataMask 为十六进制字符串。</summary>
    public Task SetIoDeviceOutputDataAsync(string network, string device, int startByte, string signalData,
        string dataMask, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["startbyte"] = startByte.ToString(),
            ["signaldata"] = signalData,
            ["datamask"] = dataMask
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}/set-outputdata"
            : $"rw/iosystem/devices/{Escape(network)}/{Escape(device)}?action=set-outputdata";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>写组信号（GI/GO），值按整数发送。network/device 传空时按信号名直接写。</summary>
    public Task SetGroupIoAsync(string signal, int value, string? network = "Local", string? device = "DRV_1",
        CancellationToken ct = default)
    {
        var signalPath = string.IsNullOrEmpty(network) && string.IsNullOrEmpty(device)
            ? Escape(signal)
            : $"{Escape(network ?? string.Empty)}/{Escape(device ?? string.Empty)}/{Escape(signal)}";
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/signals/{signalPath}/set-value"
            : $"rw/iosystem/signals/{signalPath}?action=set";
        var form = new Dictionary<string, string>
        {
            ["lvalue"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return _protocol.PostAsync(resource, form, ct);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetConfigDictionaryAsync(string resource,
        CancellationToken ct)
    {
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.First().Values
            .Where(pair => !pair.Key.StartsWith('_'))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddIfNotEmpty(IDictionary<string, string> form, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            form[key] = value;
        }
    }
}
