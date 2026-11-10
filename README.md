# Abb.Rws.Client

面向 ABB 机器人 RWS（Robot Web Services）的 .NET 客户端，支持：

- **RWS 2.0**：OmniCore / RobotWare 7，HTTPS + Basic，XHTML
- **RWS 1.0**：IRC5 / RobotWare 6，HTTP + Digest，JSON
- 自动协议探测，也可强制指定版本

项目已在 CRB 15000 / RobotWare 7.21.502 上完成真机验证。

## 环境要求

- .NET 10 SDK
- 控制器已启用 RWS
- 可用的 RWS 用户，默认 `Default User` / `robotics`
- 写入和运动操作需要相应的 UAS 权限与控制权

## 构建

```bash
dotnet build AbbRwsClient.sln -c Release
```

## 快速开始

```csharp
using Abb.Rws.Client;

using var client = await RwsClient.ConnectAsync(new RwsClientOptions
{
    Host = "192.168.4.10",
    Username = "Default User",
    Password = "robotics"
});

Console.WriteLine($"协议 {client.Version}，内容版本 {client.ApiVersion}");
Console.WriteLine($"控制器状态：{await client.GetControllerStateAsync()}");
Console.WriteLine($"操作模式：{await client.GetOperationModeAsync()}");
Console.WriteLine($"速度倍率：{await client.GetSpeedRatioAsync()}%");
```

常用连接选项：

```csharp
var options = new RwsClientOptions
{
    Host = "192.168.4.10",
    Port = 443,
    UseHttps = true,
    Username = "Admin",
    Password = "robotics",
    Version = RwsProtocolVersion.Rws2, // Auto / Rws1 / Rws2
    VerifyCertificate = false,         // ABB 默认自签名证书
    Timeout = TimeSpan.FromSeconds(10)
};
```

`Version = Auto` 时依次尝试：

1. RWS 2.0，HTTPS 443
2. RWS 1.0，HTTP 80
3. RWS 1.0，HTTPS 443

显式指定 `http://` 或 `https://` 时只尝试对应协议。

## 常用示例

### 读取系统与版本

```csharp
var system = await client.GetSystemInfoAsync();
Console.WriteLine($"RobotWare {system.RobotWareVersion}");
Console.WriteLine($"RWS 服务：{string.Join(", ", (await client.GetRwsServicesAsync()).Select(s => s.Name))}");
Console.WriteLine($"产品版本：{string.Join(", ", (await client.GetSystemProductsAsync()).Select(p => $"{p.Name}={p.Version}"))}");
```

### 读取设备树

```csharp
var groups = await client.GetDeviceGroupsAsync();               // HW_DEVICES / SW_RESOURCES
var cpu = await client.GetDeviceAsync("HW_DEVICES/MAIN_COMPUTER/MAIN_COMPUTER_CPU");
foreach (var property in cpu.Properties)
{
    Console.WriteLine($"{property.Name} = {property.Value}{property.Unit}");
}
```

### 读写 IO

```csharp
var signal = await client.GetSignalAsync("vdo_VisionCalibTrig", "Local", "DRV_1");
Console.WriteLine($"{signal.Name} = {signal.LValue}，write-access={signal.WriteAccess}");

await client.RequestMastershipAsync(MastershipDomain.Edit);
try
{
    await client.SetDigitalIoAsync("vdo_VisionCalibTrig", true, "Local", "DRV_1");
}
finally
{
    await client.ReleaseMastershipAsync(MastershipDomain.Edit);
}
```

注意：GO/AO 以及部分安全信号在远程模式下为只读，实际以 `write-access` 为准。

### 事件日志

```csharp
var domains = await client.GetEventLogDomainsAsync();
var newest = await client.GetEventLogAsync(0, limit: 10, start: 1, order: "lifo", lang: "en");

foreach (var entry in newest)
{
    Console.WriteLine($"{entry.TimeStamp} [{entry.Code}] {entry.Title}");
}
```

`start` 是 1 基页码。RWS 2.0 默认不支持 LIFO，库会自动使用 `v=2.1`。

### WebSocket 订阅

```csharp
await using var subscription = await client.SubscribeAsync(new[]
{
    RwsSubscriptionResource.SpeedRatio(),
    RwsSubscriptionResource.OperationMode()
});

Console.WriteLine($"订阅组 {subscription.GroupId}，初始事件 {subscription.InitialEvents.Count} 条");

await foreach (var evt in subscription.Events.ReadAllAsync())
{
    Console.WriteLine($"{evt.Type} speedratio={evt.Value("speedratio")} opmode={evt.Value("opmode")}");
}
```

运行时管理订阅组：

```csharp
var groupId = subscription.GroupId!.Value;
var added = await client.AddSubscriptionResourceAsync(groupId,
    RwsSubscriptionResource.ExecutionState());
await client.SetSubscriptionResourcePriorityAsync(groupId, added.Index, 1);
await client.RemoveSubscriptionResourceAsync(groupId, added.ResourcePath);
await client.DeleteSubscriptionGroupAsync(groupId);
```

### RAPID

```csharp
await client.RequestMastershipAsync(MastershipDomain.Edit);
try
{
    await client.UploadFileAsync("$HOME/MyModule.mod", File.ReadAllBytes("MyModule.mod"));
    await client.LoadModuleAsync("$HOME/MyModule.mod", replace: true);
    await client.ExecuteRoutineAsync("MyModule", "main");

    var pointer = await client.GetProgramPointerAsync();
    Console.WriteLine($"程序指针：{pointer.ModuleName}/{pointer.RoutineName}");
}
finally
{
    await client.ReleaseMastershipAsync(MastershipDomain.Edit);
}
```

远程启动受 UAS Grant `UAS_REMOTE_START_STOP_IN_AUTO` 限制；没有该授权时，只能把程序指针准备好，由示教器按 Start。

### 运动系统

```csharp
var unit = await client.GetMechUnitAsync("ROB_1");
Console.WriteLine($"工具={unit.ToolName} 工件={unit.WorkObjectName} 负载={unit.PayloadName}");

await client.RequestMastershipAsync(MastershipDomain.Edit);
await client.RequestMastershipAsync(MastershipDomain.Motion);
try
{
    await client.SetMechUnitPayloadAsync("ROB_1", unit.PayloadName, unit.TotalPayloadName);
    await client.SetMechUnitModeAsync("ROB_1", unit.Mode);
    await client.SetMechUnitJogModeAsync("ROB_1", unit.JogMode ?? "Cartesian");
    await client.SetMechUnitCoordSystemAsync("ROB_1", unit.CoordSystem ?? "World");
}
finally
{
    await client.ReleaseMastershipAsync(MastershipDomain.Motion);
    await client.ReleaseMastershipAsync(MastershipDomain.Edit);
}
```

碰撞预测：

```csharp
var prediction = await client.GetCollisionPredictionAsync();
await client.SetCollisionPredictionModeAsync(prediction.Enabled);
```

控制器只接受 `mode=true|false`。

### 文件与备份

```csharp
var volumes = await client.ListFileVolumesAsync();
var files = await client.ListFilesAsync("$HOME");

var bytes = await client.DownloadFileAsync("$HOME/Rbt_Painting.mod");
await client.UploadFileAsync("$HOME/copy.mod", bytes);

Console.WriteLine($"备份状态：{await client.GetBackupStateAsync()}");
```

## API 覆盖

| 领域 | 主要接口 |
| --- | --- |
| 连接与协议 | `ConnectAsync`、`Version`、`ApiVersion`、`GetRwsServicesAsync`、`GetSystemProductsAsync`、`GetReturnCodesAsync`、`GetResourceOptionsAsync` |
| 系统与面板 | 控制器状态、操作模式、速度倍率、电机上下电、模式锁、碰撞检测、使能请求、时钟/时区/时间服务器/网络、备份/恢复/重启 |
| 事件日志 | 域列表、分页读取、按序号读取、LIFO、清空、原始日志保存 |
| RAPID | 任务、模块、源码、符号、变量、程序指针、例行程序执行、启停、断点、调用栈、UI 指令、服务例行程序、ModPos、Hold-to-run、Alias IO |
| 运动系统 | 机械单元、轴、轴姿态、pjoints、基座标、笛卡尔位姿、SMB、电机校准、Lead-through 读写、工具/工件/负载/模式/Jog/坐标系写入、运动监控、路径监控、变更计数、非运动执行、碰撞预测与模型、SingleUserLin 校准、能耗 |
| IO | 网络、设备、信号、两组条件高级搜索、配置、仿真、阻塞解除、DO/AO/GO 写入、设备输入输出数据 |
| 控制器诊断 | 控制器身份读取/改名、证书存储、安全状态（模式/制动检查/负载/配置/违规）、注册表、控制器选项/特性、文件压缩 |
| CFG | 域、类型、实例读取，实例创建/修改/删除，域保存，文件加载/校验 |
| 文件 | 卷、目录、上传/下载、元数据、创建目录、复制、重命名、删除 |
| DIPC | 限制信息、队列列表、创建/删除队列、发送/读取消息 |
| UAS | 用户、角色、Grants、LDAP、配置导入导出、用户/角色写操作 |
| 订阅 | 多资源 WebSocket 订阅、初始事件、组列表/详情、增删资源、优先级、删除组 |
| 设备树 | 设备分组、节点、属性读取 |

## 测试

测试需要连接真实控制器，部分模式会写入或移动机器人。

```bash
# 只读 Smoke 测试
dotnet run --project tests/Abb.Rws.Client.Smoke -- 192.168.4.10

# MoveTest 模式
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 status
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 devices
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 dipc
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 backup
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 motion
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 io
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 panel
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 subs
dotnet run --project tests/Abb.Rws.Client.MoveTest -- 192.168.4.10 protocol
```

其他模式：`rapid`、`filesys`、`motor`、`circle`、`cleanup`、`uas`。

- `circle` / `cleanup` 需要 `tests/Abb.Rws.Client.MoveTest/state.json` 保存现场状态，该文件已被 `.gitignore` 排除。
- `uas` 需要管理员账号：`<host> uas <adminUser> <adminPassword>`。

## 已知限制

- **RWS 1.0** 代码已实现，但没有 IRC5 真机验证。
- **RWS 2.1** 仅事件日志支持，其他资源使用 `v=2.1` 会返回 406。
- **`rw/vision`** 视觉服务尚未实现。
- **EGM**（Externally Guided Motion）不属于 RWS，未包含在本库中。
- 仅封装 SingleUserLin 精细校准；圈数计数器、其他校准方法仍未封装。
- 轴姿态写入、SingleUserLin 校准属于高风险操作，未在真机执行验证。
- 当前 RobotWare 7.21 上，运动监控和路径监控只有读接口；写入返回 405。
- `ctrl/diagnostics` 在当前控制器上还没有已保存的诊断数据；`ctrl/virtualtime` 仅虚拟控制器支持。
- 安全控制器只提供只读状态查询；重置、加锁/解锁等安全写入未封装。
- Alias IO 在当前控制器上列表为空。
- GO/AO 远程写入、远程 Start 取决于控制器信号属性和 UAS 授权，不是库本身可以绕过。
- 面板语言只支持写入，控制器不提供读回接口。

## 安全说明

本库不是安全级组件。运动、IO、备份恢复、重启等操作可能改变机器人状态：

- 在测试控制器或确认安全的工作空间内运行
- 使用低速度倍率并随时准备急停
- 写入前先读取当前值，操作后恢复
- 生产环境请结合 UAS 权限、SafeMove 和现场安全规程

## 项目结构

```text
AbbRwsClient.sln
src/Abb.Rws.Client/
  RwsClient.*.cs          各 RWS 资源分组
  RwsModels.cs            公共模型
  RwsEnums.cs             枚举
  Internal/               协议层（RWS 1.0 / 2.0、Digest、XHTML 解析）
tests/
  Abb.Rws.Client.Smoke/   只读回归测试
  Abb.Rws.Client.MoveTest/现场验证与运动准备
```
