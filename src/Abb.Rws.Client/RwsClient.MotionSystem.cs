namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>列出运动系统中的机械单元。</summary>
    public async Task<IReadOnlyList<MechUnitSummary>> GetMechUnitsAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/motionsystem/mechunits", ct).ConfigureAwait(false);
        return result.Items.Select(item => new MechUnitSummary(
            item.Title ?? item.Value("name") ?? string.Empty,
            item.Value("mode") ?? string.Empty,
            item.Value("drive-module") ?? string.Empty)).ToList();
    }

    /// <summary>读取单个机械单元的详细信息。</summary>
    public async Task<MechUnitInfo> GetMechUnitAsync(string mechUnit, CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}", ct)
            .ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Value("tool-name") is not null) ?? result.First();
        return new MechUnitInfo(
            item.Title ?? mechUnit,
            item.Value("type") ?? string.Empty,
            item.Value("mode") ?? string.Empty,
            item.Value("tool-name") ?? string.Empty,
            item.Value("wobj-name") ?? string.Empty,
            item.Value("total-payload-name") ?? item.Value("payload-name") ?? string.Empty,
            ParseInt(item.Value("axes")),
            item.Value("task-name"),
            item.Value("jog-mode"),
            item.Value("status"));
    }

    /// <summary>读取机械单元各轴状态。</summary>
    public async Task<IReadOnlyList<MechUnitAxisInfo>> GetMechUnitAxesAsync(string mechUnit,
        CancellationToken ct = default)
    {
        var collection = await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/axes", ct).ConfigureAwait(false);
        var countItem = collection.Items.FirstOrDefault(i => i.Value("axes") is not null);
        var count = ParseInt(countItem?.Value("axes"));
        if (count <= 0)
        {
            return [];
        }

        var axes = new List<MechUnitAxisInfo>(count);
        for (var axis = 1; axis <= count; axis++)
        {
            var result = await _protocol
                .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/axes/{axis}", ct)
                .ConfigureAwait(false);
            var values = result.Items
                .SelectMany(i => i.Values)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            axes.Add(new MechUnitAxisInfo(
                axis,
                values.GetValueOrDefault("axis-status"),
                values.GetValueOrDefault("logical-axis"),
                values));
        }

        return axes;
    }

    /// <summary>读取机械单元物理关节值。</summary>
    public async Task<double[]> GetMechUnitPJointsAsync(string mechUnit, CancellationToken ct = default)
    {
        var item = (await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/pjoints", ct).ConfigureAwait(false)).First();
        return Numbers(item, "j1", "j2", "j3", "j4", "j5", "j6");
    }

    /// <summary>读取机械单元基座标系。</summary>
    public async Task<MechUnitBaseFrame> GetMechUnitBaseFrameAsync(string mechUnit,
        CancellationToken ct = default)
    {
        var result = await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/baseframe", ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Value("q1") is not null) ?? result.First();
        var type = result.Items.FirstOrDefault(i => i.Value("type") is not null)?.Value("type");
        return new MechUnitBaseFrame(
            Numbers(item, "x", "y", "z"),
            Numbers(item, "q1", "q2", "q3", "q4"),
            type);
    }

    /// <summary>读取机械单元柔顺引导（Lead-through）状态。</summary>
    public async Task<string> GetMechUnitLeadThroughAsync(string mechUnit, CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/motionsystem/mechunits/{Escape(mechUnit)}/lead-through"
            : $"rw/motionsystem/mechunits/{Escape(mechUnit)}?resource=lead-through";
        var item = (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First();
        return item.Value("status") ?? string.Empty;
    }

    /// <summary>读取 SMB（串行测量板）数据。</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetMechUnitSmbDataAsync(string mechUnit,
        CancellationToken ct = default)
    {
        var result = await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/smbdata", ct).ConfigureAwait(false);
        return result.First().Values
            .Where(pair => !pair.Key.StartsWith('_'))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>读取电机校准名称。</summary>
    public async Task<IReadOnlyList<MotorCalibration>> GetMechUnitMotorCalibrationAsync(string mechUnit,
        CancellationToken ct = default)
    {
        var result = await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/motorcalib", ct).ConfigureAwait(false);
        return result.Items.Select(item => new MotorCalibration(
            item.Title ?? string.Empty,
            item.Value("joint-name") ?? string.Empty,
            item.Value("calib-name") ?? string.Empty)).ToList();
    }

    /// <summary>读取任务驱动的机械单元。</summary>
    public async Task<IReadOnlyList<TaskMechUnit>> GetTaskMechUnitsAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var result = await _protocol
            .GetAsync($"rw/rapid/tasks/{task}/motion/mechunits", ct).ConfigureAwait(false);
        return result.Items.Select(item => new TaskMechUnit(
            item.Value("name") ?? item.Title ?? string.Empty,
            item.Value("mode") ?? string.Empty,
            item.Value("type") ?? string.Empty)).ToList();
    }

    /// <summary>读取任务外部轴状态。</summary>
    public async Task<TaskExternalJointState> GetTaskExternalJointStateAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var item = (await _protocol
            .GetAsync($"rw/rapid/tasks/{task}/motion/extjointstate", ct).ConfigureAwait(false)).First();
        return new TaskExternalJointState(
            item.Value("j1") ?? string.Empty,
            item.Value("j2") ?? string.Empty,
            item.Value("j3") ?? string.Empty,
            item.Value("j4") ?? string.Empty,
            item.Value("j5") ?? string.Empty,
            item.Value("j6") ?? string.Empty);
    }

    /// <summary>读取运动系统错误状态。</summary>
    public async Task<MotionErrorState> GetMotionErrorStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/motionsystem/errorstate", ct).ConfigureAwait(false)).First();
        return new MotionErrorState(
            item.Value("err-state") ?? string.Empty,
            ParseLong(item.Value("err-count")));
    }

    /// <summary>读取碰撞预测状态。</summary>
    public async Task<CollisionPredictionInfo> GetCollisionPredictionAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/motionsystem/collisionprediction", ct)
            .ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Title == "collision-prediction-mode");
        var enabled = item?.Value("collision-prediction-mode-enabled");
        return new CollisionPredictionInfo(
            enabled is not null && (enabled.Equals("true", StringComparison.OrdinalIgnoreCase) || enabled == "1"),
            null);
    }

    /// <summary>读取系统能耗状态。</summary>
    public async Task<SystemEnergyState> GetEnergyStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/system/energy", ct).ConfigureAwait(false)).First();
        return new SystemEnergyState(
            item.Value("state") ?? string.Empty,
            item.Value("energy-state") ?? string.Empty,
            ParseDouble(item.Value("interval-energy") ?? "0", "interval-energy"),
            ParseDouble(item.Value("accumulated-energy") ?? "0", "accumulated-energy"),
            item.Value("time-stamp") ?? string.Empty,
            item.Value("reset-time") ?? string.Empty);
    }

    /// <summary>设置机械单元当前工具。需要 Edit/Motion 控制权。</summary>
    public Task SetMechUnitToolAsync(string mechUnit, string tool, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["tool"] = tool };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/motionsystem/mechunits/{Escape(mechUnit)}"
            : $"rw/motionsystem/mechunits/{Escape(mechUnit)}?action=set";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>设置机械单元当前工件坐标。需要 Edit/Motion 控制权。</summary>
    public Task SetMechUnitWorkObjectAsync(string mechUnit, string wobj, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["wobj"] = wobj };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/motionsystem/mechunits/{Escape(mechUnit)}"
            : $"rw/motionsystem/mechunits/{Escape(mechUnit)}?action=set";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>
    /// 点动机械单元。RWS 2.0 的 /jog 资源；只能在手动模式、按住使能键时使用。
    /// 本方法未做模式检查，调用方必须自行确保安全。
    /// </summary>
    public Task JogAsync(double axis1, double axis2, double axis3, double axis4, double axis5, double axis6,
        int configurationCount = 0, string incrementMode = "None", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["axis1"] = axis1.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["axis2"] = axis2.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["axis3"] = axis3.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["axis4"] = axis4.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["axis5"] = axis5.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["axis6"] = axis6.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ccount"] = configurationCount.ToString(),
            ["inc-mode"] = incrementMode
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/motionsystem/jog"
            : "rw/motionsystem?action=jog";
        return _protocol.PostAsync(resource, form, ct);
    }
}
