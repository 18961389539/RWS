using System.Globalization;
using System.Text.RegularExpressions;
using Abb.Rws.Client.Internal;

namespace Abb.Rws.Client;

/// <summary>
/// 同时支持 RWS 1.0 (IRC5 / RobotWare 6) 和 RWS 2.0 (OmniCore / RobotWare 7) 的客户端。
/// 创建实例请使用 <see cref="ConnectAsync"/>，它会自动探测协议版本。
/// </summary>
public sealed partial class RwsClient : IDisposable
{
    private readonly IRwsProtocol _protocol;
    private readonly bool _verifyCertificate;

    private RwsClient(IRwsProtocol protocol, bool verifyCertificate)
    {
        _protocol = protocol;
        _verifyCertificate = verifyCertificate;
    }

    internal IRwsProtocol Protocol => _protocol;

    internal bool VerifyCertificate => _verifyCertificate;

    /// <summary>实际使用的协议版本。</summary>
    public RwsProtocolVersion Version => _protocol.Version;

    /// <summary>实际连接的控制器地址。</summary>
    public Uri BaseUri => _protocol.BaseUri;

    /// <summary>
    /// 连接控制器并自动探测 RWS 版本。
    /// </summary>
    public static async Task<RwsClient> ConnectAsync(RwsClientOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (host, port, https) = NormalizeHost(options);
        RwsException? authError = null;
        Exception? lastError = null;
        var attempts = new List<string>();

        foreach (var (version, candidatePort, candidateHttps) in BuildCandidates(options, port, https))
        {
            IRwsProtocol? protocol = null;
            var endpoint = $"{(candidateHttps ? "https" : "http")}://{host}:{candidatePort} ({version})";
            try
            {
                var baseUri = new Uri($"{(candidateHttps ? "https" : "http")}://{host}:{candidatePort}/");
                protocol = version == RwsProtocolVersion.Rws2
                    ? new Rws2Protocol(baseUri, options.Username, options.Password, options.VerifyCertificate,
                        options.Timeout)
                    : new Rws1Protocol(baseUri, options.Username, options.Password, options.VerifyCertificate,
                        options.Timeout);

                await protocol.GetAsync("rw/system", ct).ConfigureAwait(false);
                return new RwsClient(protocol, options.VerifyCertificate);
            }
            catch (RwsException ex) when (ex.HttpStatusCode is 401 or 403)
            {
                authError ??= ex;
                attempts.Add($"{endpoint} 认证失败 HTTP {ex.HttpStatusCode}");
                protocol?.Dispose();
            }
            catch (Exception ex)
            {
                lastError = ex;
                attempts.Add($"{endpoint} {ex.Message}");
                protocol?.Dispose();
            }
        }

        if (authError is not null)
        {
            throw authError;
        }

        throw new RwsException(
            $"无法连接 ABB 控制器 {host}。尝试: {string.Join("；", attempts)}", 0, null, lastError);
    }

    // ---------------------------------------------------------------------
    // 读取
    // ---------------------------------------------------------------------

    /// <summary>读取控制器状态，如 motoron / motoroff / guardstop。</summary>
    public async Task<string> GetControllerStateAsync(CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2 ? "rw/panel/ctrl-state" : "rw/panel/ctrlstate";
        return (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First().Require("ctrlstate");
    }

    /// <summary>读取操作模式，如 AUTO / MANR。</summary>
    public async Task<string> GetOperationModeAsync(CancellationToken ct = default)
    {
        return (await _protocol.GetAsync("rw/panel/opmode", ct).ConfigureAwait(false)).First().Require("opmode");
    }

    /// <summary>读取速度倍率 0-100。</summary>
    public async Task<double> GetSpeedRatioAsync(CancellationToken ct = default)
    {
        return ToDouble((await _protocol.GetAsync("rw/panel/speedratio", ct).ConfigureAwait(false)).First(),
            "speedratio");
    }

    /// <summary>读取 RAPID 执行状态。</summary>
    public async Task<RapidExecutionState> GetExecutionStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/rapid/execution", ct).ConfigureAwait(false)).First();
        return new RapidExecutionState(item.Require("ctrlexecstate"), item.Require("cycle"));
    }

    /// <summary>读取所有 RAPID 任务。</summary>
    public async Task<IReadOnlyList<AbbTask>> GetTasksAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/rapid/tasks", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Value("name") is not null)
            .Select(ParseTask)
            .ToList();
    }

    /// <summary>读取当前关节目标（度）。</summary>
    public async Task<JointTarget> GetJointTargetAsync(string mechUnit = "ROB_1",
        CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync($"rw/motionsystem/mechunits/{mechUnit}/jointtarget", ct)
            .ConfigureAwait(false)).First();
        return new JointTarget(
            Numbers(item, "rax_1", "rax_2", "rax_3", "rax_4", "rax_5", "rax_6"),
            Numbers(item, "eax_a", "eax_b", "eax_c", "eax_d", "eax_e", "eax_f"));
    }

    /// <summary>读取当前笛卡尔目标（mm / 四元数）。</summary>
    public async Task<RobTarget> GetRobTargetAsync(string mechUnit = "ROB_1", string tool = "tool0",
        string wobj = "wobj0", string coordinate = "Base", CancellationToken ct = default)
    {
        var resource =
            $"rw/motionsystem/mechunits/{mechUnit}/robtarget?tool={tool}&wobj={wobj}&coordinate={coordinate}";
        var item = (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First();
        var extKeys = Version == RwsProtocolVersion.Rws2
            ? new[] { "eax_a", "eax_b", "eax_c", "eax_d", "eax_e", "eax_f" }
            : new[] { "eaxa", "eaxb", "eaxc", "eaxd", "eaxe", "eaxf" };
        return new RobTarget(
            Numbers(item, "x", "y", "z"),
            Numbers(item, "q1", "q2", "q3", "q4"),
            Numbers(item, "cf1", "cf4", "cf6", "cfx"),
            Numbers(item, extKeys));
    }

    /// <summary>读取单个 IO 信号。</summary>
    public async Task<AbbSignal> GetSignalAsync(string signal, string network = "Local", string device = "DRV_1",
        CancellationToken ct = default)
    {
        var resource =
            $"rw/iosystem/signals/{Escape(network)}/{Escape(device)}/{Escape(signal)}";
        var item = (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First();
        return ParseSignal(item);
    }

    /// <summary>读取 IO 信号列表（默认最多 100 条）。</summary>
    public async Task<IReadOnlyList<AbbSignal>> GetSignalsAsync(int limit = 100,
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"rw/iosystem/signals?limit={limit}", ct).ConfigureAwait(false);
        return result.Items.Select(ParseSignal).ToList();
    }

    /// <summary>读取数字量输入/输出。</summary>
    public async Task<int> GetDigitalIoAsync(string signal, string network = "Local", string device = "DRV_1",
        CancellationToken ct = default)
    {
        var value = (await GetSignalAsync(signal, network, device, ct).ConfigureAwait(false)).LValue;
        return (int)ParseDouble(value, signal);
    }

    /// <summary>读取模拟量输入/输出。</summary>
    public async Task<double> GetAnalogIoAsync(string signal, string network = "Local", string device = "DRV_1",
        CancellationToken ct = default)
    {
        var value = (await GetSignalAsync(signal, network, device, ct).ConfigureAwait(false)).LValue;
        return ParseDouble(value, signal);
    }

    /// <summary>搜索 RAPID 持久变量。</summary>
    public async Task<IReadOnlyList<RapidSymbol>> GetRapidSymbolsAsync(string task = "T_ROB1",
        bool persistentOnly = true, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["view"] = "block",
            ["vartyp"] = "any",
            ["blockurl"] = $"RAPID/{task}",
            ["symtyp"] = persistentOnly ? "per" : "any",
            ["recursive"] = "true",
            ["skipshared"] = "FALSE",
            ["onlyused"] = "FALSE",
            ["stack"] = "0",
            ["posl"] = "0",
            ["posc"] = "0"
        };

        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/rapid/symbols/search"
            : "rw/rapid/symbols?action=search-symbols";
        var result = await _protocol.PostAsync(resource, form, ct).ConfigureAwait(false);

        return result.Items.Select(item => new RapidSymbol(
            item.Value("symburl") ?? item.Title ?? string.Empty,
            item.Value("name") ?? string.Empty,
            item.Value("dattyp") ?? string.Empty,
            ParseInt(item.Value("ndim")),
            item.Value("typurl"))).ToList();
    }

    /// <summary>
    /// 读取 RAPID 变量原始字符串。symbol 可以是 "Module1/var"，也可以是完整 "RAPID/T_ROB1/Module1/var"。
    /// </summary>
    public async Task<string> GetRapidVariableAsync(string symbol, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var symbolUrl = NormalizeSymbolUrl(symbol, task);
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/symbol/{symbolUrl}/data"
            : $"rw/rapid/symbol/data/{symbolUrl}";
        return (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First().Require("value");
    }

    /// <summary>读取 RAPID num 变量。</summary>
    public async Task<double> GetRapidVariableNumAsync(string symbol, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        return ParseDouble(await GetRapidVariableAsync(symbol, task, ct).ConfigureAwait(false), symbol);
    }

    /// <summary>读取 RAPID num 数组变量。</summary>
    public async Task<double[]> GetRapidVariableNumArrayAsync(string symbol, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        return ParseRapidArray(await GetRapidVariableAsync(symbol, task, ct).ConfigureAwait(false), symbol);
    }

    /// <summary>读取 RAPID jointtarget 变量。</summary>
    public async Task<JointTarget> GetRapidVariableJointTargetAsync(string symbol, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        return ParseJointTarget(await GetRapidVariableAsync(symbol, task, ct).ConfigureAwait(false), symbol);
    }

    /// <summary>读取 RAPID jointtarget 数组变量。</summary>
    public async Task<IReadOnlyList<JointTarget>> GetRapidVariableJointTargetArrayAsync(string symbol,
        string task = "T_ROB1", CancellationToken ct = default)
    {
        return ParseJointTargetArray(await GetRapidVariableAsync(symbol, task, ct).ConfigureAwait(false), symbol);
    }

    /// <summary>读取 RAPID robtarget 变量。</summary>
    public async Task<RobTarget> GetRapidVariableRobTargetAsync(string symbol, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        return ParseRobTarget(await GetRapidVariableAsync(symbol, task, ct).ConfigureAwait(false), symbol);
    }

    /// <summary>读取 RAMDISK 路径。</summary>
    public async Task<string> GetRamdiskPathAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("ctrl/$RAMDISK", ct).ConfigureAwait(false)).First();
        return item.Value("value") ?? item.Value("_value")
            ?? throw new RwsException("RWS 响应缺少 RAMDISK 路径");
    }

    /// <summary>读取事件日志。</summary>
    public Task<IReadOnlyList<AbbEventLogEntry>> GetEventLogAsync(int domain = 0, int limit = 50,
        CancellationToken ct = default)
    {
        return GetEventLogAsync(domain, limit, 1, "fifo", "en", ct);
    }

    /// <summary>读取事件日志。start 为 1 基页码；order=lifo 需要 RWS 2.1。</summary>
    public async Task<IReadOnlyList<AbbEventLogEntry>> GetEventLogAsync(int domain, int limit, int start,
        string order, string lang, CancellationToken ct = default)
    {
        if (start < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "事件日志页码从 1 开始");
        }

        var query = $"?lang={Uri.EscapeDataString(lang)}&limit={limit}&start={start}" +
                    $"&order={Uri.EscapeDataString(order)}";
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/elog/{domain}{query}"
            : $"rw/elog/{domain}/{query}";

        // RWS 2.0 不支持 LIFO 排序，控制器要求改用 v=2.1。
        var result = order.Equals("lifo", StringComparison.OrdinalIgnoreCase) &&
                     Version == RwsProtocolVersion.Rws2
            ? await _protocol.GetAsync(resource, "application/xhtml+xml;v=2.1", ct).ConfigureAwait(false)
            : await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.Items.Select(ParseEventLog).ToList();
    }

    /// <summary>读取控制权状态。</summary>
    public async Task<string> GetMastershipStateAsync(MastershipDomain domain = MastershipDomain.Edit,
        CancellationToken ct = default)
    {
        if (domain == MastershipDomain.All)
        {
            throw new ArgumentException("查询状态请指定 Edit 或 Motion", nameof(domain));
        }

        var path = Version == RwsProtocolVersion.Rws2
            ? (domain == MastershipDomain.Motion ? "motion" : "edit")
            : (domain == MastershipDomain.Motion ? "motion" : "rapid");
        var item = (await _protocol.GetAsync($"rw/mastership/{path}", ct).ConfigureAwait(false)).First();
        return item.Value("mastership") ?? item.Title ?? string.Empty;
    }

    /// <summary>列出控制器文件。</summary>
    public async Task<IReadOnlyList<AbbFileInfo>> ListFilesAsync(string path = "$HOME",
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("fileservice/" + EscapePath(path), ct).ConfigureAwait(false);
        return result.Items.Select(item => new AbbFileInfo(
            item.Title ?? string.Empty,
            (item.Type ?? string.Empty).Contains("dir", StringComparison.OrdinalIgnoreCase),
            ParseNullableLong(item.Value("fs-size")))).ToList();
    }

    /// <summary>下载控制器文件。</summary>
    public Task<byte[]> DownloadFileAsync(string path, CancellationToken ct = default)
    {
        return _protocol.GetBytesAsync("fileservice/" + EscapePath(path), ct);
    }

    // ---------------------------------------------------------------------
    // 写入
    // ---------------------------------------------------------------------

    /// <summary>申请控制权。写操作前必须调用。</summary>
    public async Task RequestMastershipAsync(MastershipDomain domain = MastershipDomain.Edit,
        CancellationToken ct = default)
    {
        foreach (var resource in MastershipResources(domain, request: true))
        {
            await _protocol.PostAsync(resource, null, ct).ConfigureAwait(false);
        }
    }

    /// <summary>释放控制权。</summary>
    public async Task ReleaseMastershipAsync(MastershipDomain domain = MastershipDomain.Edit,
        CancellationToken ct = default)
    {
        foreach (var resource in MastershipResources(domain, request: false))
        {
            await _protocol.PostAsync(resource, null, ct).ConfigureAwait(false);
        }
    }

    /// <summary>设置速度倍率 0-100。需要 Motion 控制权。</summary>
    public Task SetSpeedRatioAsync(double ratio, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["speed-ratio"] = ratio.ToString(CultureInfo.InvariantCulture)
        };
        return _protocol.PostAsync("rw/panel/speedratio?action=setspeedratio", form, ct);
    }

    /// <summary>写数字量输出。需要 Edit 控制权。network/device 传空时按信号名直接写。</summary>
    public Task SetDigitalIoAsync(string signal, bool value, string? network = "Local", string? device = "DRV_1",
        CancellationToken ct = default)
    {
        var signalPath = string.IsNullOrEmpty(network) && string.IsNullOrEmpty(device)
            ? Escape(signal)
            : $"{Escape(network ?? string.Empty)}/{Escape(device ?? string.Empty)}/{Escape(signal)}";
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/iosystem/signals/{signalPath}/set-value"
            : $"rw/iosystem/signals/{signalPath}?action=set";
        var form = new Dictionary<string, string> { ["lvalue"] = value ? "1" : "0" };
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>写模拟量输出。需要 Edit 控制权。network/device 传空时按信号名直接写。</summary>
    public Task SetAnalogIoAsync(string signal, double value, string? network = "Local", string? device = "DRV_1",
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
            ["mode"] = "value",
            ["lvalue"] = value.ToString(CultureInfo.InvariantCulture)
        };
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>写 RAPID 变量。需要 Edit 控制权。</summary>
    public Task SetRapidVariableAsync(string symbol, string value, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var symbolUrl = NormalizeSymbolUrl(symbol, task);
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/symbol/{symbolUrl}/data?action=set"
            : $"rw/rapid/symbol/data/{symbolUrl}?action=set";
        var form = new Dictionary<string, string> { ["value"] = value };
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>启动 RAPID 程序。</summary>
    public Task StartAsync(string cycle = "asis", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["regain"] = "continue",
            ["execmode"] = "continue",
            ["cycle"] = cycle,
            ["condition"] = "none",
            ["stopatbp"] = "disabled",
            ["alltaskbytsp"] = "true"
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/rapid/execution/start"
            : "rw/rapid/execution?action=start";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>停止 RAPID 程序。</summary>
    public Task StopAsync(CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["stopmode"] = "stop" };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/rapid/execution/stop"
            : "rw/rapid/execution?action=stop";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>重置程序指针到 main。</summary>
    public Task ResetProgramPointerAsync(CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/rapid/execution/resetpp"
            : "rw/rapid/execution?action=resetpp";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>上传文件到控制器。需要 Edit 控制权。</summary>
    public Task UploadFileAsync(string path, byte[] content, CancellationToken ct = default)
    {
        return _protocol.PutBytesAsync("fileservice/" + EscapePath(path), content, ct);
    }

    /// <summary>删除控制器文件。需要 Edit 控制权。</summary>
    public Task DeleteFileAsync(string path, CancellationToken ct = default)
    {
        return _protocol.DeleteAsync("fileservice/" + EscapePath(path), ct);
    }

    // ---------------------------------------------------------------------
    // 内部实现
    // ---------------------------------------------------------------------

    private IEnumerable<string> MastershipResources(MastershipDomain domain, bool request)
    {
        var action = request ? "request" : "release";
        if (Version == RwsProtocolVersion.Rws2)
        {
            yield return domain switch
            {
                MastershipDomain.Motion => $"rw/mastership/motion/{action}",
                MastershipDomain.All => $"rw/mastership/{action}",
                _ => $"rw/mastership/edit/{action}"
            };
        }
        else
        {
            switch (domain)
            {
                case MastershipDomain.Motion:
                    yield return $"rw/mastership/motion?action={action}";
                    break;
                case MastershipDomain.All:
                    yield return $"rw/mastership?action={action}";
                    break;
                default:
                    yield return $"rw/mastership/cfg?action={action}";
                    yield return $"rw/mastership/rapid?action={action}";
                    break;
            }
        }
    }

    private static AbbTask ParseTask(RwsItem item) => new(
        item.Require("name"),
        item.Value("type") ?? string.Empty,
        item.Value("taskstate") ?? string.Empty,
        item.Value("excstate") ?? string.Empty,
        IsTrue(item.Value("active")) || string.Equals(item.Value("active"), "On", StringComparison.OrdinalIgnoreCase),
        IsTrue(item.Value("motiontask")));

    private static AbbSignal ParseSignal(RwsItem item) => new(
        item.Value("name") ?? item.Title ?? string.Empty,
        item.Value("type") ?? string.Empty,
        item.Value("lvalue") ?? string.Empty,
        item.Value("lstate"),
        item.Value("access-level"),
        item.Value("write-access"));

    private static AbbEventLogEntry ParseEventLog(RwsItem item)
    {
        var sequence = 0L;
        if (!string.IsNullOrEmpty(item.Title))
        {
            var lastSegment = item.Title.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            _ = long.TryParse(lastSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);
        }

        return new AbbEventLogEntry(
            sequence,
            ParseInt(item.Value("msgtype")),
            ParseLong(item.Value("code")),
            item.Value("tstamp") ?? string.Empty,
            item.Value("title") ?? string.Empty,
            item.Value("desc") ?? string.Empty,
            item.Value("conseqs") ?? string.Empty,
            item.Value("causes") ?? string.Empty,
            item.Value("actions") ?? string.Empty);
    }

    private static string NormalizeSymbolUrl(string symbol, string task)
    {
        var trimmed = symbol.Trim().TrimStart('/');
        return trimmed.StartsWith("RAPID/", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"RAPID/{task}/{trimmed}";
    }

    private static double ToDouble(RwsItem item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (item.Value(key) is { } text)
            {
                return ParseDouble(text, key);
            }
        }

        throw new RwsException($"RWS 响应缺少数值字段 '{string.Join("/", keys)}'");
    }

    private static double ParseDouble(string text, string field) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new RwsException($"无法把字段 '{field}' 的值 '{text}' 解析为数字");

    private static double[] Numbers(RwsItem item, params string[] keys) =>
        keys.Select(key => ToDouble(item, key)).ToArray();

    private static double[] ParseRapidArray(string text, string symbol)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
        {
            throw new RwsException($"变量 '{symbol}' 的值不是数组：{text}");
        }

        var inner = trimmed[1..^1].Trim();
        if (inner.Length == 0)
        {
            return [];
        }

        return inner.Split(',')
            .Select(part => ParseDouble(part.Trim(), symbol))
            .ToArray();
    }

    private static JointTarget ParseJointTarget(string text, string symbol)
    {
        var match = Regex.Match(text, @"^\s*\[\s*\[([^\]]*)\]\s*,\s*\[([^\]]*)\]\s*\]\s*$");
        if (!match.Success)
        {
            throw new RwsException($"变量 '{symbol}' 的值不是 jointtarget：{text}");
        }

        return new JointTarget(ParseNumberList(match.Groups[1].Value, symbol),
            ParseNumberList(match.Groups[2].Value, symbol));
    }

    private static IReadOnlyList<JointTarget> ParseJointTargetArray(string text, string symbol)
    {
        var matches = Regex.Matches(text, @"\[\[([^\]]*)\],\[([^\]]*)\]\]");
        if (matches.Count == 0)
        {
            throw new RwsException($"变量 '{symbol}' 的值不是 jointtarget 数组：{text}");
        }

        return matches.Cast<Match>().Select(match => new JointTarget(
            ParseNumberList(match.Groups[1].Value, symbol),
            ParseNumberList(match.Groups[2].Value, symbol))).ToList();
    }

    private static RobTarget ParseRobTarget(string text, string symbol)
    {
        var match = Regex.Match(text,
            @"^\s*\[\s*\[([^\]]*)\]\s*,\s*\[([^\]]*)\]\s*,\s*\[([^\]]*)\]\s*,\s*\[([^\]]*)\]\s*\]\s*$");
        if (!match.Success)
        {
            throw new RwsException($"变量 '{symbol}' 的值不是 robtarget：{text}");
        }

        return new RobTarget(
            ParseNumberList(match.Groups[1].Value, symbol),
            ParseNumberList(match.Groups[2].Value, symbol),
            ParseNumberList(match.Groups[3].Value, symbol),
            ParseNumberList(match.Groups[4].Value, symbol));
    }

    private static double[] ParseNumberList(string text, string symbol)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        return trimmed.Split(',')
            .Select(part => ParseDouble(part.Trim(), symbol))
            .ToArray();
    }

    private static int ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static long ParseLong(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static long? ParseNullableLong(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool IsTrue(string? text) =>
        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) ||
        text == "1";

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string EscapePath(string path) =>
        string.Join('/', path.Trim('/').Split('/').Select(Uri.EscapeDataString));

    private static (string Host, int? Port, bool? Https) NormalizeHost(RwsClientOptions options)
    {
        if (Uri.TryCreate(options.Host, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return (uri.Host, uri.IsDefaultPort ? options.Port : uri.Port, uri.Scheme == Uri.UriSchemeHttps);
        }

        return (options.Host, options.Port, options.UseHttps);
    }

    private static List<(RwsProtocolVersion Version, int Port, bool Https)> BuildCandidates(
        RwsClientOptions options, int? port, bool? https)
    {
        var candidates = new List<(RwsProtocolVersion, int, bool)>();
        switch (options.Version)
        {
            case RwsProtocolVersion.Rws2:
                candidates.Add((RwsProtocolVersion.Rws2, port ?? 443, true));
                break;
            case RwsProtocolVersion.Rws1:
                var useHttps = https ?? false;
                candidates.Add((RwsProtocolVersion.Rws1, port ?? (useHttps ? 443 : 80), useHttps));
                break;
            default:
                if (https == true)
                {
                    candidates.Add((RwsProtocolVersion.Rws2, port ?? 443, true));
                    candidates.Add((RwsProtocolVersion.Rws1, port ?? 443, true));
                }
                else if (https == false)
                {
                    candidates.Add((RwsProtocolVersion.Rws1, port ?? 80, false));
                }
                else
                {
                    candidates.Add((RwsProtocolVersion.Rws2, port ?? 443, true));
                    candidates.Add((RwsProtocolVersion.Rws1, port ?? 80, false));
                    candidates.Add((RwsProtocolVersion.Rws1, port ?? 443, true));
                }

                break;
        }

        return candidates;
    }

    public void Dispose() => _protocol.Dispose();
}
