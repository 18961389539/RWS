namespace Abb.Rws.Client;

/// <summary>RAPID 执行状态。</summary>
public sealed record RapidExecutionState(string CtrlExecState, string Cycle);

/// <summary>RAPID 任务状态。</summary>
public sealed record AbbTask(string Name, string Type, string TaskState, string ExecState, bool Active, bool MotionTask);

/// <summary>
/// 关节目标。角度单位为度，与 RWS 返回一致。
/// </summary>
public sealed record JointTarget(double[] RobAxDeg, double[] ExtAxDeg);

/// <summary>
/// 笛卡尔目标。平移单位为毫米，旋转为四元数 [w,x,y,z]。
/// </summary>
public sealed record RobTarget(double[] TransMm, double[] RotQuaternion, double[] RobConf, double[] ExtAxDeg);

/// <summary>IO 信号状态。</summary>
public sealed record AbbSignal(string Name, string Type, string LValue, string? LState,
    string? AccessLevel = null, string? WriteAccess = null);

/// <summary>RAPID 持久变量描述。</summary>
public sealed record RapidSymbol(string SymbolUrl, string Name, string DataType, int Dimensions, string? TypeUrl);

/// <summary>控制器事件日志条目。</summary>
public sealed record AbbEventLogEntry(
    long SequenceNumber,
    int MessageType,
    long Code,
    string TimeStamp,
    string Title,
    string Description,
    string Consequences,
    string Causes,
    string Actions);

/// <summary>文件或目录信息。</summary>
public sealed record AbbFileInfo(string Name, bool IsDirectory, long? Size);

/// <summary>文件元数据。</summary>
public sealed record RwsFileMetadata(long? Length, string? ContentType, DateTimeOffset? LastModified,
    string? ETag);

/// <summary>文件服务卷。</summary>
public sealed record FileVolumeInfo(string Name, string? Type);

/// <summary>控制器时钟。</summary>
public sealed record ControllerClockInfo(string DateTime);

/// <summary>控制器网络接口。</summary>
public sealed record NetworkInterfaceInfo(string Port, string Name, string NetworkType, string Address,
    string Mask, bool Dhcp, string? Gateway);

/// <summary>控制器系统信息。</summary>
public sealed record RwsSystemInfo(
    string Name,
    int Major,
    int Minor,
    int Build,
    int Revision,
    string Title,
    string Type,
    string Description,
    string BuildDate,
    int SubRevision = 0,
    string? BuildTag = null,
    int RobApiCompatibilityRevision = 0,
    string? RwVersion = null,
    string? SystemId = null,
    string? StartTime = null,
    string? RwVersionName = null,
    string? RwDistributionVersion = null)
{
    public string RobotWareVersion => $"{Major}.{Minor}.{Build}";
}

/// <summary>RWS 根资源下的一项服务。</summary>
public sealed record RwsServiceInfo(string Name, string Href);

/// <summary>控制器产品版本，如 RobotControl 7.21.0。</summary>
public sealed record RwsProductVersion(string Name, string Version, string VersionName);

/// <summary>RWS 返回码说明。</summary>
public sealed record RwsReturnCode(string Name, long Code, string Severity, string Description);

/// <summary>控制器设备树节点。</summary>
public sealed record RwsDeviceInfo(string Id, string Name, string Path,
    IReadOnlyList<RwsDeviceInfo> Children, IReadOnlyList<RwsDeviceProperty> Properties);

/// <summary>控制器设备属性。</summary>
public sealed record RwsDeviceProperty(string Id, string Name, string Value, string? Unit);

/// <summary>资源支持的 HTTP 方法及原始 OPTIONS 响应。</summary>
public sealed record RwsResourceOptions(IReadOnlyList<string> Allow, string? RawBody);

/// <summary>RAPID 模块信息。</summary>
public sealed record RapidModuleInfo(string Name, string Type, string? FileName, string? Attribute);

/// <summary>RAPID 程序指针。</summary>
public sealed record ProgramPointerInfo(string? ModuleName, string? RoutineName, string? BeginPosition,
    string? EndPosition, uint ChangeCount);

/// <summary>构建/语法错误。</summary>
public sealed record RapidBuildError(string ModuleName, int Row, int Column, string Error);

/// <summary>RAPID 断点。</summary>
public sealed record RapidBreakpoint(string ModuleName, int Row, int Column);

/// <summary>RAPID 调用栈帧。</summary>
public sealed record ActivationRecord(string ExecutionLevel, int BeginRow, int BeginColumn, int EndRow,
    int EndColumn, string? StackUrl, string? RoutineUrl);

/// <summary>RAPID 服务例行程序。</summary>
public sealed record RapidServiceRoutine(string Name, string Url, bool IsServiceRoutine);

/// <summary>当前活动的 UI 指令。</summary>
public sealed record RapidUiInstruction(string? Title, string? Href, IReadOnlyDictionary<string, string> Values);

/// <summary>任务选择状态。</summary>
public sealed record TaskSelectionInfo(string Name, string State, bool MotionTask, bool UserModify);

/// <summary>任务结构变更计数。</summary>
public sealed record TaskStructuralChangeCount(long ChangeCount, long StructureChangeCount);

/// <summary>机械单元概要。</summary>
public sealed record MechUnitSummary(string Name, string Mode, string DriveModule);

/// <summary>机械单元详细信息。</summary>
public sealed record MechUnitInfo(string Name, string Type, string Mode, string ToolName, string WorkObjectName,
    string PayloadName, int AxisCount, string? TaskName, string? JogMode, string? Status,
    string? TotalPayloadName = null, string? CoordSystem = null);

/// <summary>机械单元单轴状态。</summary>
public sealed record MechUnitAxisInfo(int Axis, string? Status, string? LogicalAxis,
    IReadOnlyDictionary<string, string> Values);

/// <summary>机械单元基座标系。</summary>
public sealed record MechUnitBaseFrame(double[] TransMm, double[] RotQuaternion, string? Type);

/// <summary>任务驱动的机械单元。</summary>
public sealed record TaskMechUnit(string Name, string Mode, string Type);

/// <summary>任务外部轴状态。</summary>
public sealed record TaskExternalJointState(string J1, string J2, string J3, string J4, string J5, string J6);

/// <summary>运动系统错误状态。</summary>
public sealed record MotionErrorState(string ErrorState, long ErrorCount);

/// <summary>碰撞预测状态。</summary>
public sealed record CollisionPredictionInfo(bool Enabled, string? ModelName);

/// <summary>电机校准名称。</summary>
public sealed record MotorCalibration(string Joint, string JointName, string CalibrationName);

/// <summary>系统能耗状态。</summary>
public sealed record SystemEnergyState(string State, string EnergyMode, double IntervalEnergy,
    double AccumulatedEnergy, string TimeStamp, string ResetTime);

/// <summary>DIPC IPC 消息。</summary>
public sealed record IpcMessage(string QueueName, string Data, string Command, string UserDef, string MessageType);

/// <summary>DIPC 服务限制。</summary>
public sealed record IpcInfo(long MaxBodySize, long MaxPacketSize);

/// <summary>DIPC 队列信息。</summary>
public sealed record IpcQueueInfo(string QueueName, string Data, string Command, string UserDef, string MessageType);

/// <summary>RMMP 状态。</summary>
public sealed record RmmpState(string UserId, string Privilege, bool HeldByMe);

/// <summary>UAS 角色。</summary>
public sealed record UasRoleInfo(string Name, string? Description);

/// <summary>UAS 授权项。</summary>
public sealed record UasGrantInfo(string Name, string? DisplayName, string? Description);

/// <summary>UAS 用户详情。</summary>
public sealed record UasUserDetails(string Name, string? Description, bool Enabled, bool PasswordChangeAllowed);

/// <summary>事件日志域信息。</summary>
public sealed record ElogDomainInfo(int Domain, long NumEvents, long BufferSize);

/// <summary>当前会话登录信息。</summary>
public sealed record RwsLoginInfo(string UserName, string UserLocale, string Application, string UserId);

/// <summary>IO 网络。</summary>
public sealed record IoNetworkInfo(string Name, string? LogicalState, string? PhysicalState);

/// <summary>IO 设备。</summary>
public sealed record IoDeviceInfo(string Name, string Network, string? LogicalState, string? PhysicalState,
    string? Address);

/// <summary>CFG 类型信息。</summary>
public sealed record CfgTypeInfo(string Name, int AttributeCount, int InstanceCount);

/// <summary>订阅资源。</summary>
public sealed record RwsSubscriptionResource(string ResourcePath, int Priority = 0)
{
    public static RwsSubscriptionResource ControllerState(int priority = 0) =>
        new("/rw/panel/ctrlstate", priority);

    public static RwsSubscriptionResource OperationMode(int priority = 0) =>
        new("/rw/panel/opmode", priority);

    public static RwsSubscriptionResource SpeedRatio(int priority = 0) =>
        new("/rw/panel/speedratio", priority);

    public static RwsSubscriptionResource ExecutionState(int priority = 0) =>
        new("/rw/rapid/execution;ctrlexecstate", priority);

    public static RwsSubscriptionResource Signal(string signal, string network = "Local", string device = "DRV_1",
        int priority = 1) =>
        new($"/rw/iosystem/signals/{network}/{device}/{signal};state", priority);

    public static RwsSubscriptionResource RapidVariable(string symbol, string task = "T_ROB1",
        int priority = 2) =>
        new(symbol.StartsWith("RAPID/", StringComparison.OrdinalIgnoreCase)
            ? $"/rw/rapid/symbol/{symbol}/data;value"
            : $"/rw/rapid/symbol/RAPID/{task}/{symbol.TrimStart('/')}/data;value", priority);

    public static RwsSubscriptionResource EventLog(int priority = 0) => new("/rw/elog/0", priority);

    public static RwsSubscriptionResource IpcQueue(string queueName, int priority = 0) =>
        new($"/rw/dipc/{queueName}", priority);
}

/// <summary>活动订阅组摘要。</summary>
public sealed record RwsSubscriptionGroup(int Id, string? EventsUrl);

/// <summary>订阅组内的资源条目。</summary>
public sealed record RwsSubscriptionEntry(int Index, string ResourcePath);

/// <summary>订阅事件。</summary>
public sealed record RwsEvent(string Type, string? Title, string? Href, IReadOnlyDictionary<string, string> Values)
{
    public string? Value(string key) => Values.TryGetValue(key, out var value) ? value : null;
}

