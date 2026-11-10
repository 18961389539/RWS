using System.Globalization;

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
            item.Value("payload-name") ?? item.Value("total-payload-name") ?? string.Empty,
            ParseInt(item.Value("axes")),
            item.Value("task-name"),
            item.Value("jog-mode"),
            item.Value("status"),
            item.Value("total-payload-name"),
            item.Value("coord-system"),
            ParseInt(item.Value("axes-total")),
            IsIntegratedUnit(item.Value("is-integrated-unit")),
            IsIntegratedUnit(item.Value("has-integrated-unit")));
    }

    /// <summary>读取运动系统摘要（变更计数、机械单元、轮询周期、绝对精度、模态负载）。</summary>
    public async Task<MotionSystemSummary> GetMotionSystemSummaryAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/motionsystem", ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(entry => entry.Value("change-count") is not null)
                   ?? result.First();
        return new MotionSystemSummary(
            ParseLong(item.Value("change-count")),
            item.Value("mechunit-name") ?? string.Empty,
            ParseInt(item.Value("poll-rate")),
            IsTrue(item.Value("absacc-active")),
            IsTrue(item.Value("modal-payload-mode")));
    }

    /// <summary>
    /// 检查指定的运动变更计数是否仍然有效。
    /// 返回 true 表示计数未变化，false 表示运动配置已变化。
    /// </summary>
    public async Task<bool> IsMotionChangeCountValidAsync(long changeCount,
        CancellationToken ct = default)
    {
        var item = (await _protocol
            .GetAsync($"rw/motionsystem/checkchangecount/{changeCount}", ct).ConfigureAwait(false)).First();
        return IsTrue(item.Value("change-state"));
    }

    /// <summary>读取非运动执行模式（如 ON / OFF）。</summary>
    public async Task<string> GetNonMotionExecutionModeAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/motionsystem/nonmotionexecution", ct)
            .ConfigureAwait(false)).First();
        return (item.Value("mode") ?? string.Empty).Trim('"');
    }

    /// <summary>设置非运动执行模式。</summary>
    public Task SetNonMotionExecutionModeAsync(string mode, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["mode"] = mode };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/motionsystem/nonmotionexecution"
            : "rw/motionsystem/nonmotionexecution?action=set-mode";
        return _protocol.PostAsync(resource, form, ct);
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

    /// <summary>读取机械单元单轴姿态（轴坐标系位置与四元数）。</summary>
    public async Task<MechUnitAxisPose> GetMechUnitAxisPoseAsync(string mechUnit, int axis,
        CancellationToken ct = default)
    {
        var item = (await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/axes/{axis}/pose", ct)
            .ConfigureAwait(false)).First();
        return new MechUnitAxisPose(
            Numbers(item, "x", "y", "z"),
            Numbers(item, "q1", "q2", "q3", "q4"));
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

    /// <summary>设置柔顺引导（Lead-through）状态。高风险，会改变机器人柔顺行为。</summary>
    public Task SetMechUnitLeadThroughAsync(string mechUnit, string status,
        bool disableAutoLoadCompensation = false, string? softness = null, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["status"] = status,
            ["disableautoloadcompensation"] = disableAutoLoadCompensation ? "1" : "0"
        };
        if (!string.IsNullOrEmpty(softness))
        {
            form["softness"] = softness;
        }

        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/motionsystem/mechunits/{Escape(mechUnit)}/lead-through"
            : $"rw/motionsystem/mechunits/{Escape(mechUnit)}/lead-through?action=set";
        return _protocol.PostAsync(resource, form, ct);
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

    /// <summary>
    /// SingleUserLin 精细校准。每个点包含 8 个值 [x,y,z,q1,q2,q3,q4,axis-value]，至少 3 个点。
    /// 极高风险，会改写机器人校准数据。
    /// </summary>
    public Task CalibrateSingleUserLinAsync(string mechUnit, double tolerance,
        IReadOnlyList<double[]> points, CancellationToken ct = default)
    {
        if (points.Count < 3)
        {
            throw new ArgumentException("至少需要 3 个校准点", nameof(points));
        }

        var form = new Dictionary<string, string>
        {
            ["method"] = "SingleUserLin",
            ["type"] = "SINGLE",
            ["tolerance"] = tolerance.ToString(CultureInfo.InvariantCulture)
        };
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.Length != 8)
            {
                throw new ArgumentException("每个校准点必须包含 8 个值", nameof(points));
            }

            form[$"point{index + 1}"] =
                "[" + string.Join(", ", point.Select(value => value.ToString(CultureInfo.InvariantCulture))) + "]";
        }

        return _protocol.PostAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/calib", form, ct);
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

    /// <summary>读取机械单元的碰撞预测模型。</summary>
    public async Task<CollisionPredictionModel> GetCollisionPredictionModelAsync(
        string mechUnit = "ROB_1", CancellationToken ct = default)
    {
        var item = (await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/coll-pred-model", ct)
            .ConfigureAwait(false)).First();
        return new CollisionPredictionModel(
            item.Value("model-name") ?? string.Empty,
            IsTrue(item.Value("initialized-flag")));
    }

    /// <summary>读取机械单元运动监控（Jog Supervision）状态。</summary>
    public async Task<MotionSupervisionInfo> GetMotionSupervisionAsync(string mechUnit = "ROB_1",
        CancellationToken ct = default)
    {
        var item = (await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/motionsupervision", ct)
            .ConfigureAwait(false)).First();
        return new MotionSupervisionInfo(
            IsTrue(item.Value("mode-enabled")),
            ParseInt(item.Value("level")));
    }

    /// <summary>读取机械单元路径监控状态。</summary>
    public async Task<PathSupervisionInfo> GetPathSupervisionAsync(string mechUnit = "ROB_1",
        CancellationToken ct = default)
    {
        var item = (await _protocol
            .GetAsync($"rw/motionsystem/mechunits/{Escape(mechUnit)}/pathsupervision", ct)
            .ConfigureAwait(false)).First();
        return new PathSupervisionInfo(
            item.Value("mode") ?? string.Empty,
            ParseInt(item.Value("level")));
    }

    /// <summary>启用/停用碰撞预测（Collision Prediction）。</summary>
    public Task SetCollisionPredictionModeAsync(bool enabled, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["mode"] = enabled ? "true" : "false" };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/motionsystem/collisionprediction"
            : "rw/motionsystem/motionsupervision?action=set-colpred-mode";
        return _protocol.PostAsync(resource, form, ct);
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
    public Task SetMechUnitToolAsync(string mechUnit, string tool, CancellationToken ct = default) =>
        SetMechUnitAttributeAsync(mechUnit, new Dictionary<string, string> { ["tool"] = tool }, ct);

    /// <summary>设置机械单元当前工件坐标。需要 Edit/Motion 控制权。</summary>
    public Task SetMechUnitWorkObjectAsync(string mechUnit, string wobj, CancellationToken ct = default) =>
        SetMechUnitAttributeAsync(mechUnit, new Dictionary<string, string> { ["wobj"] = wobj }, ct);

    /// <summary>设置机械单元负载。totalPayload 为空时只写 payload。</summary>
    public Task SetMechUnitPayloadAsync(string mechUnit, string payload, string? totalPayload = null,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["payload"] = payload };
        if (!string.IsNullOrEmpty(totalPayload))
        {
            form["total-payload"] = totalPayload;
        }

        return SetMechUnitAttributeAsync(mechUnit, form, ct);
    }

    /// <summary>设置机械单元模式，如 Activated。</summary>
    public Task SetMechUnitModeAsync(string mechUnit, string mode, CancellationToken ct = default) =>
        SetMechUnitAttributeAsync(mechUnit, new Dictionary<string, string> { ["mode"] = mode }, ct);

    /// <summary>设置机械单元 Jog 模式，如 Cartesian / Joint。</summary>
    public Task SetMechUnitJogModeAsync(string mechUnit, string jogMode, CancellationToken ct = default) =>
        SetMechUnitAttributeAsync(mechUnit, new Dictionary<string, string> { ["jog-mode"] = jogMode }, ct);

    /// <summary>设置机械单元坐标系，如 World / Base / Tool / Wobj。</summary>
    public Task SetMechUnitCoordSystemAsync(string mechUnit, string coordSystem,
        CancellationToken ct = default) =>
        SetMechUnitAttributeAsync(mechUnit, new Dictionary<string, string> { ["coord-system"] = coordSystem }, ct);

    /// <summary>设置机械单元单轴姿态。高风险，会改写轴坐标系。</summary>
    public Task SetMechUnitAxisPoseAsync(string mechUnit, int axis, double x, double y, double z,
        double q1, double q2, double q3, double q4, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["x"] = x.ToString(CultureInfo.InvariantCulture),
            ["y"] = y.ToString(CultureInfo.InvariantCulture),
            ["z"] = z.ToString(CultureInfo.InvariantCulture),
            ["q1"] = q1.ToString(CultureInfo.InvariantCulture),
            ["q2"] = q2.ToString(CultureInfo.InvariantCulture),
            ["q3"] = q3.ToString(CultureInfo.InvariantCulture),
            ["q4"] = q4.ToString(CultureInfo.InvariantCulture)
        };
        return _protocol.PostAsync(
            $"rw/motionsystem/mechunits/{Escape(mechUnit)}/axes/{axis}/pose", form, ct);
    }

    private Task SetMechUnitAttributeAsync(string mechUnit, IReadOnlyDictionary<string, string> form,
        CancellationToken ct)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/motionsystem/mechunits/{Escape(mechUnit)}"
            : $"rw/motionsystem/mechunits/{Escape(mechUnit)}?action=set";
        return _protocol.PostAsync(resource, form, ct);
    }

    private static bool IsIntegratedUnit(string? value) =>
        !string.IsNullOrEmpty(value) &&
        !value.StartsWith("No", StringComparison.OrdinalIgnoreCase);

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
