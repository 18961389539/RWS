using System.Text;
using System.Text.Json;
using Abb.Rws.Client;

const string ModuleName = "ZZ_MOVE_DELTA";
const string RemotePath = "$HOME/ZZ_MOVE_DELTA.mod";
var StatePath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "state.json"));

var host = args.Length > 0 ? args[0] : "192.168.4.10";
var mode = args.Length > 1 ? args[1] : "circle";

if (mode.Equals("uas", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 4)
    {
        Console.WriteLine("用法: <host> uas <adminUser> <adminPassword>");
        return;
    }

    using var admin = await RwsClient.ConnectAsync(new RwsClientOptions
    {
        Host = host,
        Username = args[2],
        Password = args[3]
    });
    await TestUasAdminAsync(admin);
    return;
}

const string moduleText = """
MODULE ZZ_MOVE_DELTA
  PROC Circle30mm100()
    VAR robtarget p0;
    VAR robtarget pStart;
    VAR num i;
    p0 := CRobT(\Tool:=tool0 \WObj:=wobj0);
    pStart := Offs(p0, 15, 0, 0);
    MoveL pStart, v100, fine, tool0;
    FOR i FROM 1 TO 100 DO
      MoveC Offs(p0, 0, 15, 0), Offs(p0, -15, 0, 0), v100, fine, tool0;
      MoveC Offs(p0, 0, -15, 0), Offs(p0, 15, 0, 0), v100, fine, tool0;
    ENDFOR
    MoveL p0, v100, fine, tool0;
  ENDPROC
ENDMODULE
""";

using var client = await RwsClient.ConnectAsync(new RwsClientOptions { Host = host });
Console.WriteLine($"连接：{client.Version} {client.BaseUri}");

if (mode.Equals("circle", StringComparison.OrdinalIgnoreCase))
{
    await PrepareCircleAsync();
}
else if (mode.Equals("cleanup", StringComparison.OrdinalIgnoreCase))
{
    await CleanupAsync();
}
else if (mode.Equals("status", StringComparison.OrdinalIgnoreCase))
{
    await PrintStatusAsync();
}
else if (mode.Equals("motor", StringComparison.OrdinalIgnoreCase))
{
    await TestMotorAsync();
}
else if (mode.Equals("rapid", StringComparison.OrdinalIgnoreCase))
{
    await TestRapidAdvancedAsync();
}
else if (mode.Equals("motion", StringComparison.OrdinalIgnoreCase))
{
    await TestMotionSystemAsync();
}
else if (mode.Equals("io", StringComparison.OrdinalIgnoreCase))
{
    await TestIoAdvancedAsync();
}
else if (mode.Equals("filesys", StringComparison.OrdinalIgnoreCase))
{
    await TestFilesSystemAsync();
}
else if (mode.Equals("panel", StringComparison.OrdinalIgnoreCase))
{
    await TestPanelLogAsync();
}
else if (mode.Equals("subs", StringComparison.OrdinalIgnoreCase))
{
    await TestSubscriptionsAsync();
}
else if (mode.Equals("protocol", StringComparison.OrdinalIgnoreCase))
{
    await TestProtocolAsync();
}
else if (mode.Equals("devices", StringComparison.OrdinalIgnoreCase))
{
    await TestDevicesAsync();
}
else if (mode.Equals("dipc", StringComparison.OrdinalIgnoreCase))
{
    await TestDipcQueuesAsync();
}
else if (mode.Equals("backup", StringComparison.OrdinalIgnoreCase))
{
    await TestBackupAsync();
}
else if (mode.Equals("ctrl", StringComparison.OrdinalIgnoreCase))
{
    await TestControllerAsync();
}
else
{
    Console.WriteLine("用法: <host> circle|cleanup|status|motor|rapid|motion|io|filesys|panel|subs|protocol|devices|dipc|backup|ctrl");
}

async Task PrepareCircleAsync()
{
    if (!File.Exists(StatePath))
    {
        Console.WriteLine($"FAIL 找不到现场文件 {StatePath}，无法确定原始程序指针");
        return;
    }

    var state = JsonSerializer.Deserialize<SavedState>(await File.ReadAllTextAsync(StatePath));
    if (state is null)
    {
        Console.WriteLine("FAIL 现场文件无效");
        return;
    }

    var loaded = false;
    try
    {
        await client.RequestMastershipAsync(MastershipDomain.Edit);

        // 先把程序指针移出临时模块，才能替换模块内容
        try
        {
            await client.ExecuteRoutineAsync(state.Module, state.Routine);
        }
        catch
        {
            await client.ResetProgramPointerAsync();
        }

        await client.UploadFileAsync(RemotePath, Encoding.UTF8.GetBytes(moduleText));
        await client.LoadModuleAsync(RemotePath, replace: true);
        loaded = true;
        Console.WriteLine("PASS 上传并加载 Circle30mm100");

        var routines = await client.GetRapidModuleRoutinesAsync(ModuleName);
        if (!routines.Contains("Circle30mm100", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模块中没有 Circle30mm100");
        }

        await client.RequestMastershipAsync(MastershipDomain.Motion);
        await client.SetSpeedRatioAsync(20);
        Console.WriteLine("PASS 速度倍率设为 20%");

        await client.ExecuteRoutineAsync(ModuleName, "Circle30mm100");
        var pointer = await client.GetProgramPointerAsync();
        Console.WriteLine($"PASS 程序指针: {pointer.ModuleName}/{pointer.RoutineName}");
        Console.WriteLine("准备完成：水平圆 XY，圆心=当前 TCP，直径 30mm，连续 100 圈");
        Console.WriteLine("等待示教器按 Start；预计约 8 分钟");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL 准备失败: {ex.GetType().Name}: {ex.Message}");
        if (loaded)
        {
            try
            {
                await client.RequestMastershipAsync(MastershipDomain.Motion);
                await client.StopAsync();
                await client.SetSpeedRatioAsync(state.SpeedRatio);
                await client.ExecuteRoutineAsync(state.Module, state.Routine);
                await client.UnloadModuleAsync(ModuleName);
                await client.DeleteFileAsync(RemotePath);
                Console.WriteLine("已回滚到原状态");
            }
            catch (Exception rollbackEx)
            {
                Console.WriteLine($"WARN 回滚失败: {rollbackEx.Message}");
            }
        }
    }
    finally
    {
        try
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
        catch
        {
            // 忽略
        }
    }
}

async Task CleanupAsync()
{
    if (!File.Exists(StatePath))
    {
        Console.WriteLine($"FAIL 找不到现场文件 {StatePath}");
        return;
    }

    var state = JsonSerializer.Deserialize<SavedState>(await File.ReadAllTextAsync(StatePath));
    if (state is null)
    {
        Console.WriteLine("FAIL 现场文件无效");
        return;
    }

    try
    {
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);

        try
        {
            await client.StopAsync();
        }
        catch
        {
            // 已经停止
        }

        await client.SetSpeedRatioAsync(state.SpeedRatio);
        Console.WriteLine($"PASS 恢复速度 {state.SpeedRatio}%");

        try
        {
            await client.ExecuteRoutineAsync(state.Module, state.Routine);
        }
        catch
        {
            await client.ResetProgramPointerAsync();
        }
        var pointer = await client.GetProgramPointerAsync();
        Console.WriteLine($"PASS 恢复程序指针: {pointer.ModuleName}/{pointer.RoutineName}");

        try
        {
            await client.UnloadModuleAsync(ModuleName);
            Console.WriteLine("PASS 卸载 ZZ_MOVE_DELTA");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN 卸载模块失败: {ex.Message}");
        }

        try
        {
            await client.DeleteFileAsync(RemotePath);
            Console.WriteLine("PASS 删除临时文件");
        }
        catch
        {
            // 文件可能不存在
        }
    }
    finally
    {
        try
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
            Console.WriteLine("PASS 释放控制权");
        }
        catch
        {
            // 忽略
        }
    }

    await PrintStatusAsync();
    var finalTcp = await client.GetRobTargetAsync();
    var delta = finalTcp.TransMm.Zip(state.Tcp, (a, b) => a - b).ToArray();
    Console.WriteLine($"TCP 偏差: dX={delta[0]:F3} dY={delta[1]:F3} dZ={delta[2]:F3} mm");
    if (delta.Any(d => Math.Abs(d) > 1.0))
    {
        Console.WriteLine("WARN TCP 与准备前偏差超过 1mm，说明运行被中途停止或未完成");
    }
}

async Task TestMotorAsync()
{
    var execution = await client.GetExecutionStateAsync();
    if (execution.CtrlExecState.Equals("running", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("FAIL 程序正在运行，拒绝电机上下电测试");
        return;
    }

    var before = await client.GetRobTargetAsync();
    var stateBefore = await client.GetControllerStateAsync();
    Console.WriteLine($"测试前: {stateBefore}, TCP Z={before.TransMm[2]:F3}");
    if (!stateBefore.Equals("motoron", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"FAIL 当前不是 motoron，而是 {stateBefore}");
        return;
    }

    var held = false;
    try
    {
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);
        held = true;
        Console.WriteLine("PASS 获取 Edit/Motion 控制权");

        await client.SetControllerStateAsync(false);
        var off = await WaitForStateAsync("motoroff", 15);
        Console.WriteLine(off ? "PASS motoroff" : "FAIL motoroff");

        if (off)
        {
            await Task.Delay(2000);
            var mid = await client.GetRobTargetAsync();
            var dz = mid.TransMm[2] - before.TransMm[2];
            Console.WriteLine($"抱闸保持检查: dZ={dz:F3} mm");

            await client.SetControllerStateAsync(true);
            var on = await WaitForStateAsync("motoron", 20);
            Console.WriteLine(on ? "PASS motoron" : "FAIL motoron");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL 电机测试: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        try
        {
            var state = await client.GetControllerStateAsync();
            if (!state.Equals("motoron", StringComparison.OrdinalIgnoreCase))
            {
                await client.SetControllerStateAsync(true);
                await WaitForStateAsync("motoron", 20);
                Console.WriteLine("WARN 已再次尝试上电");
            }
        }
        catch
        {
            // 忽略
        }

        if (held)
        {
            try
            {
                await client.ReleaseMastershipAsync(MastershipDomain.Motion);
                await client.ReleaseMastershipAsync(MastershipDomain.Edit);
                Console.WriteLine("PASS 释放控制权");
            }
            catch
            {
                // 忽略
            }
        }
    }

    var after = await client.GetRobTargetAsync();
    var stateAfter = await client.GetControllerStateAsync();
    Console.WriteLine($"测试后: {stateAfter}, TCP Z={after.TransMm[2]:F3}, " +
                      $"dZ={after.TransMm[2] - before.TransMm[2]:F3} mm");
}

async Task<bool> WaitForStateAsync(string expected, int timeoutSeconds)
{
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (DateTime.UtcNow < deadline)
    {
        var state = await client.GetControllerStateAsync();
        if (state.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await Task.Delay(500);
    }

    return false;
}

async Task TestRapidAdvancedAsync()
{
    await Try("alias io", async () => (await client.GetRapidAliasIoAsync()).Count);
    await Try("breakpoints", async () => (await client.GetBreakpointsAsync()).Count);
    await Try("activation record", () => client.GetActivationRecordAsync());
    await Try("motion pointer", () => client.GetMotionPointerAsync());
    await Try("program pointer sync", () => client.GetProgramPointerSyncStateAsync());
    await Try("motion pointer sync", () => client.GetMotionPointerSyncStateAsync());
    await Try("structural change count", () => client.GetStructuralChangeCountAsync());
    await Try("task selection", async () => (await client.GetTaskSelectionAsync()).Count);
    await Try("service routines", async () => (await client.GetServiceRoutinesAsync()).Count);
    await Try("active ui instruction", () => client.GetActiveUiInstructionAsync());
    await Try("module text range", async () =>
        (await client.GetModuleTextRangeAsync("Wizard_Data", 1, 1, 3, -1)).Length);
    await Try("execution error state", () => client.GetExecutionErrorStateAsync());

    // 安全写测试：把 PP 设置到当前已有的 MinimalVisionCalibration/main
    await Try("set PP to current routine", async () =>
    {
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        try
        {
            await client.SetProgramPointerAsync("MinimalVisionCalibration", "main");
            var pointer = await client.GetProgramPointerAsync();
            return $"{pointer.ModuleName}/{pointer.RoutineName}";
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    });
}

async Task Try<T>(string name, Func<Task<T>> action)
{
    try
    {
        var result = await action();
        Console.WriteLine($"PASS {name}: {Format(result)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

static string Format(object? value) => value switch
{
    null => "<null>",
    string text => text,
    ActivationRecord record => $"{record.ExecutionLevel} {record.BeginRow},{record.BeginColumn}-" +
                               $"{record.EndRow},{record.EndColumn} {record.RoutineUrl}",
    ProgramPointerInfo pointer => $"{pointer.ModuleName}/{pointer.RoutineName} " +
                                  $"{pointer.BeginPosition}",
    TaskStructuralChangeCount count => $"{count.ChangeCount}/{count.StructureChangeCount}",
    System.Collections.IEnumerable items => string.Join(", ", items.Cast<object?>().Select(x => x?.ToString())),
    _ => value.ToString() ?? string.Empty
};

async Task TestMotionSystemAsync()
{
    await Try("mechunits", async () => string.Join(", ",
        (await client.GetMechUnitsAsync()).Select(u => $"{u.Name}:{u.Mode}")));
    await Try("mechunit ROB_1", () => client.GetMechUnitAsync("ROB_1"));
    await Try("mechunit axes", async () => string.Join(", ",
        (await client.GetMechUnitAxesAsync("ROB_1")).Select(a => $"{a.Axis}:{a.Status}")));
    await Try("pjoints", async () => string.Join(", ", await client.GetMechUnitPJointsAsync("ROB_1")));
    await Try("base frame", () => client.GetMechUnitBaseFrameAsync("ROB_1"));
    await Try("cartesian", () => client.GetMechUnitCartesianAsync("ROB_1"));
    await Try("lead-through", () => client.GetMechUnitLeadThroughAsync("ROB_1"));
    await Try("smb data fields", async () => (await client.GetMechUnitSmbDataAsync("ROB_1")).Count);
    await Try("motor calibration", async () =>
        (await client.GetMechUnitMotorCalibrationAsync("ROB_1")).Count);
    await Try("task mechunits", async () => string.Join(", ",
        (await client.GetTaskMechUnitsAsync()).Select(u => $"{u.Name}:{u.Mode}")));
    await Try("task ext joint state", () => client.GetTaskExternalJointStateAsync());
    await Try("motion error state", () => client.GetMotionErrorStateAsync());
    await Try("collision prediction", () => client.GetCollisionPredictionAsync());
    await Try("collision prediction model", () => client.GetCollisionPredictionModelAsync("ROB_1"));
    await Try("energy state", () => client.GetEnergyStateAsync());
    await Try("motion system summary", () => client.GetMotionSystemSummaryAsync());
    await Try("motion change count", async () =>
    {
        var summary = await client.GetMotionSystemSummaryAsync();
        var valid = await client.IsMotionChangeCountValidAsync(summary.ChangeCount);
        return $"change-count={summary.ChangeCount} valid={valid}";
    });
    await Try("non-motion execution", () => client.GetNonMotionExecutionModeAsync());
    await Try("motion supervision", () => client.GetMotionSupervisionAsync("ROB_1"));
    await Try("path supervision", () => client.GetPathSupervisionAsync("ROB_1"));
    await Try("axis 1 pose", () => client.GetMechUnitAxisPoseAsync("ROB_1", 1));

    // 安全写测试：Lead-through 保持 Inactive
    await Try("set lead-through to current", async () =>
    {
        var status = await client.GetMechUnitLeadThroughAsync("ROB_1");
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);
        try
        {
            await client.SetMechUnitLeadThroughAsync("ROB_1", status, false, "0,0,0,0,0,0,0");
            return await client.GetMechUnitLeadThroughAsync("ROB_1");
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    });

    // 安全写测试：非运动执行模式保持原值
    await Try("set non-motion execution to current", async () =>
    {
        var mode = await client.GetNonMotionExecutionModeAsync();
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        try
        {
            await client.SetNonMotionExecutionModeAsync(mode);
            return mode;
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    });

    // 安全写测试：把当前工具/工件坐标设置成原值
    await Try("set tool/wobj to current", async () =>
    {
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);
        try
        {
            await client.SetMechUnitToolAsync("ROB_1", "tool0");
            await client.SetMechUnitWorkObjectAsync("ROB_1", "wobj0");
            var info = await client.GetMechUnitAsync("ROB_1");
            return $"{info.ToolName}/{info.WorkObjectName}";
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    });

    // 安全写测试：把负载/模式/Jog 模式/坐标系设置成原值
    await Try("set motion attributes to current", async () =>
    {
        var before = await client.GetMechUnitAsync("ROB_1");
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);
        try
        {
            await client.SetMechUnitPayloadAsync("ROB_1", before.PayloadName, before.TotalPayloadName);
            await client.SetMechUnitModeAsync("ROB_1", before.Mode);
            if (!string.IsNullOrEmpty(before.JogMode))
            {
                await client.SetMechUnitJogModeAsync("ROB_1", before.JogMode);
            }

            if (!string.IsNullOrEmpty(before.CoordSystem))
            {
                await client.SetMechUnitCoordSystemAsync("ROB_1", before.CoordSystem);
            }

            var after = await client.GetMechUnitAsync("ROB_1");
            return $"payload={after.PayloadName}/{after.TotalPayloadName} mode={after.Mode} " +
                   $"jog={after.JogMode} coord={after.CoordSystem}";
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    });

    // 安全写测试：碰撞预测保持原状态
    await Try("set collision prediction to current", async () =>
    {
        var before = await client.GetCollisionPredictionAsync();
        await client.SetCollisionPredictionModeAsync(before.Enabled);
        var after = await client.GetCollisionPredictionAsync();
        return $"enabled {before.Enabled} -> {after.Enabled}";
    });
}

async Task TestIoAdvancedAsync()
{
    var networks = await client.GetIoNetworksAsync();
    var devices = await client.GetIoDevicesAsync();
    var firstNetwork = networks.FirstOrDefault()?.Name ?? "IntBus";
    var firstDevice = devices.FirstOrDefault();

    await Try("io network", async () => (await client.GetIoNetworkAsync(firstNetwork)).ToString());
    await Try("io network config", async () =>
        (await client.GetIoNetworkConfigAsync(firstNetwork)).Count);
    await Try("io device config", async () =>
        (await client.GetIoDeviceConfigAsync(firstDevice?.Network ?? "IntBus",
            firstDevice?.Name ?? "Asi")).Count);
    await Try("signal config", async () =>
        (await client.GetSignalConfigAsync("SafetyEnable", "SC_Feedback_Net", "SC_Feedback_Dev")).Count);
    await Try("search signals type GO", async () =>
    {
        var groups = await client.SearchSignalsAsync(type: "GO");
        var first = groups.FirstOrDefault();
        var access = first is null ? null : (await client.GetSignalByNameAsync(first.Name)).WriteAccess;
        return $"{groups.Count}，样本 access={access}";
    });
    await Try("search signals advanced", async () =>
        (await client.SearchSignalsAsync(type: "DO", blocked: false, invert: false)).Count);
    await Try("search devices enabled", async () =>
        (await client.SearchIoDevicesAsync(logicalState: "enabled")).Count);

    // 安全写测试：DO 虚拟信号同值回写
    await Try("set DO by name to current", async () =>
    {
        var signal = await client.GetSignalByNameAsync("vdo_VisionCalibTrig");
        if (signal.WriteAccess is not null &&
            !signal.WriteAccess.Contains("All", StringComparison.OrdinalIgnoreCase) &&
            !signal.WriteAccess.Contains("LocalAuto", StringComparison.OrdinalIgnoreCase))
        {
            return $"跳过：write-access={signal.WriteAccess}";
        }

        await client.RequestMastershipAsync(MastershipDomain.Edit);
        try
        {
            var value = signal.LValue == "1";
            await client.SetDigitalIoAsync(signal.Name, value, null, null);
            var readback = await client.GetSignalByNameAsync(signal.Name);
            return $"{signal.Name}={readback.LValue}";
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    });

    // GO 组信号：先检查远程写权限，不可写则跳过
    await Try("set group IO to current", async () =>
    {
        var groups = await client.SearchSignalsAsync(type: "GO");
        var target = groups.FirstOrDefault(g => g.LValue == "0");
        if (target is null)
        {
            return "没有值为 0 的 GO，跳过";
        }

        var full = await client.GetSignalByNameAsync(target.Name);
        if (full.WriteAccess is null ||
            !full.WriteAccess.Contains("All", StringComparison.OrdinalIgnoreCase))
        {
            return $"跳过：write-access={full.WriteAccess}";
        }

        await client.RequestMastershipAsync(MastershipDomain.Edit);
        try
        {
            await client.SetGroupIoAsync(target.Name, 0, null, null);
            var readback = await client.GetSignalByNameAsync(target.Name);
            return $"{target.Name}={readback.LValue}";
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    });
}

async Task TestFilesSystemAsync()
{
    await Try("file volumes", async () => string.Join(", ",
        (await client.ListFileVolumesAsync()).Select(v => v.Name)));
    await Try("file metadata", () => client.GetFileMetadataAsync("$HOME/Rbt_Painting.mod"));
    await Try("system clock", () => client.GetSystemClockAsync());
    await Try("time zone", () => client.GetTimeZoneAsync());
    await Try("time server", () => client.GetTimeServerAsync());
    await Try("network settings", async () => string.Join(", ",
        (await client.GetNetworkSettingsAsync()).Select(n => $"{n.Name}:{n.Address}")));

    var dirName = "dotnet_fs_" + DateTime.Now.ToString("HHmmss");
    var dir = $"$HOME/{dirName}";
    try
    {
        await client.CreateDirectoryAsync("$HOME", dirName);
        Console.WriteLine($"PASS create directory {dirName}");

        var source = $"{dir}/a.txt";
        var payload = System.Text.Encoding.UTF8.GetBytes("hello abb rws " + DateTime.Now);
        await client.UploadFileAsync(source, payload);
        Console.WriteLine("PASS upload a.txt");

        var meta = await client.GetFileMetadataAsync(source);
        Console.WriteLine($"PASS metadata: {meta.Length} bytes");

        var downloaded = await client.DownloadFileAsync(source);
        Console.WriteLine(downloaded.SequenceEqual(payload)
            ? "PASS download round-trip"
            : "FAIL download content mismatch");

        await client.CopyFileAsync(source, "b.txt", overwrite: true);
        Console.WriteLine("PASS copy a.txt -> b.txt");

        await client.RenameFileAsync($"{dir}/b.txt", "c.txt");
        Console.WriteLine("PASS rename b.txt -> c.txt");

        await client.DeleteFileAsync($"{dir}/a.txt");
        await client.DeleteFileAsync($"{dir}/c.txt");
        Console.WriteLine("PASS delete temp files");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL file operations: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        try
        {
            await client.DeleteDirectoryAsync(dir);
            Console.WriteLine("PASS delete temp directory");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARN cleanup directory: {ex.Message}");
        }
    }
}

async Task TestUasAdminAsync(RwsClient admin)
{
    await Try("uas users", async () => string.Join(", ", await admin.ListUasUsersAsync()));
    await Try("uas roles", async () => string.Join(", ",
        (await admin.ListUasRolesAsync()).Select(r => r.Name)));
    await Try("role description", () => admin.GetUasRoleDescriptionAsync("Operator"));
    await Try("role grants", async () => (await admin.GetUasRoleGrantsAsync("Operator")).Count);
    await Try("user details", () => admin.GetUasUserAsync("Default User"));
    await Try("all grants", async () => (await admin.ListAllUasGrantsAsync()).Count);
    await Try("ldap resources", async () => string.Join(", ", await admin.ListLdapAsync()));
    await Try("uas configuration size", async () =>
        (await admin.DownloadUasConfigurationAsync()).Length);
    await Try("current user grants", async () => (await admin.GetUserGrantsAsync()).Count);
}

async Task TestPanelLogAsync()
{
    // 只读验证：不调用 SetOperationMode / Lock / Unlock / SetPanelLanguage / SaveEventLogRaw
    await Try("operation mode lock-state", () => client.GetOperationModeLockStateAsync());
    await Try("collision detection state", () => client.GetCollisionDetectionStateAsync());
    await Try("enable request state", () => client.GetEnableRequestStateAsync());

    var domains = await client.GetEventLogDomainsAsync();
    Console.WriteLine("PASS event log domains: " + (domains.Count == 0 ? "<空>" :
        string.Join(", ", domains.Select(d => $"域{d.Domain}={d.NumEvents}/{d.BufferSize}"))));

    await Try("event log fifo page 1", async () =>
    {
        var entries = await client.GetEventLogAsync(0, 3, 1, "fifo", "en");
        return string.Join(" | ", entries.Select(e => $"{e.SequenceNumber}:{e.Code}:{e.Title}"));
    });
    await Try("event log lifo limit 2", async () =>
    {
        var entries = await client.GetEventLogAsync(0, 2, 1, "lifo", "en");
        return string.Join(" | ", entries.Select(e => $"{e.SequenceNumber}:{e.Code}:{e.Title}"));
    });
    await Try("event log default overload", async () =>
        (await client.GetEventLogAsync(0, 2)).Count);

    try
    {
        var first = await client.GetEventLogAsync(0, 1, 1, "fifo", "en");
        if (first.Count > 0)
        {
            var seqnum = first[0].SequenceNumber;
            await Try($"event log by seqnum {seqnum}", async () =>
            {
                var entry = await client.GetEventLogBySequenceNumberAsync(seqnum);
                return $"{entry.SequenceNumber}:{entry.Code}:{entry.Title}";
            });
            await Try($"event log message {seqnum}", async () =>
            {
                var entry = await client.GetEventLogMessageAsync(0, seqnum);
                return $"{entry.TimeStamp} {entry.Title}";
            });
        }
        else
        {
            Console.WriteLine("WARN 域 0 没有日志，跳过按序号读取");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL 按序号读取: {ex.GetType().Name}: {ex.Message}");
    }
}

async Task TestSubscriptionsAsync()
{
    var subscription = await client.SubscribeAsync(new[]
    {
        RwsSubscriptionResource.SpeedRatio(),
        RwsSubscriptionResource.OperationMode()
    });
    var groupId = subscription.GroupId;
    Console.WriteLine($"PASS subscribe: 组={groupId?.ToString() ?? "<null>"} " +
                      $"初始事件={subscription.InitialEvents.Count} " +
                      string.Join(" | ", subscription.InitialEvents.Select(e => $"{e.Type}={e.Value("speedratio") ?? e.Value("opmode")}")));
    if (groupId is null)
    {
        Console.WriteLine("FAIL 响应中没有订阅组编号");
        await subscription.DisposeAsync();
        return;
    }

    try
    {
        await Try("list groups", async () => string.Join(", ",
            (await client.GetSubscriptionGroupsAsync()).Select(g => $"组{g.Id}->{g.EventsUrl}")));
        await Try("group resources", async () => string.Join(", ",
            (await client.GetSubscriptionGroupAsync(groupId.Value)).Select(e => $"{e.Index}:{e.ResourcePath}")));

        var added = await client.AddSubscriptionResourceAsync(groupId.Value,
            RwsSubscriptionResource.ExecutionState());
        Console.WriteLine($"PASS add resource: {added.Index}:{added.ResourcePath}");

        await Try("group after add", async () => string.Join(", ",
            (await client.GetSubscriptionGroupAsync(groupId.Value)).Select(e => $"{e.Index}:{e.ResourcePath}")));
        await Try("set priority", async () =>
        {
            await client.SetSubscriptionResourcePriorityAsync(groupId.Value, added.Index, 1);
            return $"索引 {added.Index} -> 1";
        });

        await client.RemoveSubscriptionResourceAsync(groupId.Value, added.ResourcePath);
        Console.WriteLine($"PASS remove resource: {added.ResourcePath}");
        await Try("group after remove", async () => string.Join(", ",
            (await client.GetSubscriptionGroupAsync(groupId.Value)).Select(e => $"{e.Index}:{e.ResourcePath}")));

        // 排空初始事件，再触发一次速度变化验证实时推送。
        for (var i = 0; i < subscription.InitialEvents.Count; i++)
        {
            await subscription.Events.ReadAsync();
        }

        var current = await client.GetSpeedRatioAsync();
        var probe = current > 0 ? current - 1 : current + 1;
        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);
        try
        {
            await client.SetSpeedRatioAsync(probe);
            await Task.Delay(1000);
            await client.SetSpeedRatioAsync(current);
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            while (true)
            {
                var evt = await subscription.Events.ReadAsync(timeout.Token);
                if (evt.Type.Contains("speedratio", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"PASS subscription event: {evt.Type}={evt.Value("speedratio")}");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("FAIL subscription event: 8 秒内没有收到速度事件");
        }
    }
    finally
    {
        await subscription.DisposeAsync();
    }

    await Try("groups after dispose", async () =>
    {
        var groups = await client.GetSubscriptionGroupsAsync();
        return groups.Count == 0 ? "<空>" : string.Join(", ", groups.Select(g => g.Id));
    });
}

async Task TestProtocolAsync()
{
    Console.WriteLine($"PASS protocol: {client.Version} API={client.ApiVersion} {client.BaseUri}");

    await Try("rws services", async () => string.Join(", ",
        (await client.GetRwsServicesAsync()).Select(s => s.Name)));

    var info = await client.GetSystemInfoAsync();
    Console.WriteLine($"PASS system info: RobotWare {info.RobotWareVersion} " +
                      $"rwversion={info.RwVersion} name={info.RwVersionName} " +
                      $"distribution={info.RwDistributionVersion} compat={info.RobApiCompatibilityRevision} " +
                      $"sub-revision={info.SubRevision} sysid={info.SystemId} start={info.StartTime}");

    await Try("system products", async () => string.Join(", ",
        (await client.GetSystemProductsAsync()).Select(p => $"{p.Name}={p.Version}")));
    await Try("return code 294912", async () =>
    {
        var code = await client.GetReturnCodeAsync(294912);
        return code is null ? "<null>" : $"{code.Name} [{code.Severity}] {code.Description}";
    });
    await Try("return code list", async () => (await client.GetReturnCodesAsync()).Count);
    await Try("options rw/elog", async () =>
    {
        var options = await client.GetResourceOptionsAsync("rw/elog");
        return $"Allow={string.Join(",", options.Allow)} body={options.RawBody?.Length ?? 0}B";
    });
    await Try("options subscription", async () =>
    {
        var options = await client.GetResourceOptionsAsync("subscription");
        return $"Allow={string.Join(",", options.Allow)} body={options.RawBody?.Length ?? 0}B";
    });
    await Try("api version negotiation", async () =>
    {
        await client.GetEventLogAsync(0, 1, 1, "lifo", "en");
        var lifo = client.ApiVersion;
        await client.GetEventLogAsync(0, 1, 1, "fifo", "en");
        return $"lifo={lifo} fifo={client.ApiVersion}";
    });
}

async Task TestDevicesAsync()
{
    await Try("device groups", async () =>
    {
        var groups = await client.GetDeviceGroupsAsync();
        return string.Join(", ", groups.Children.Select(child => $"{child.Id}({child.Name})"));
    });
    await Try("hardware devices", async () =>
    {
        var hw = await client.GetDeviceAsync("HW_DEVICES");
        return string.Join(", ", hw.Children.Select(child => child.Name));
    });
    await Try("ROB1 children", async () =>
    {
        var rob = await client.GetDeviceAsync("HW_DEVICES/ROB1");
        return $"{rob.Children.Count} 项: " + string.Join(", ", rob.Children.Select(child => child.Name));
    });
    await Try("main computer module info", async () =>
    {
        var device = await client.GetDeviceAsync("HW_DEVICES/MAIN_COMPUTER/MAIN_COMPUTER_MODULE_INFO");
        return string.Join("; ", device.Properties.Select(property => $"{property.Name}={property.Value}"));
    });
    await Try("CPU temperature", async () =>
    {
        var property = await client.GetDevicePropertyAsync(
            "HW_DEVICES/MAIN_COMPUTER/MAIN_COMPUTER_CPU", "PROPERTY_MC_MODULE_TEMPERATURE");
        return property is null ? "<null>" : $"{property.Name}={property.Value}{property.Unit}";
    });
    await Try("RobAPI subscriptions", async () =>
    {
        var device = await client.GetDeviceAsync("SW_RESOURCES/ROBAPI/ROBAPI_SUB_TOTAL");
        return string.Join("; ", device.Properties.Select(property => $"{property.Name}={property.Value}"));
    });
}

async Task TestDipcQueuesAsync()
{
    await Try("dipc info", () => client.GetIpcInfoAsync());
    await Try("dipc queues", async () => string.Join(", ", await client.GetIpcQueuesAsync()));

    var queue = "dotnet_q_" + DateTime.Now.ToString("HHmmss");
    try
    {
        await client.CreateIpcQueueAsync(queue, 10, 100);
        var afterCreate = await client.GetIpcQueuesAsync();
        Console.WriteLine(afterCreate.Contains(queue)
            ? $"PASS create queue {queue}"
            : $"FAIL create queue {queue}");

        await client.DeleteIpcQueueAsync(queue);
        var afterDelete = await client.GetIpcQueuesAsync();
        Console.WriteLine(afterDelete.Contains(queue)
            ? $"FAIL delete queue {queue}"
            : $"PASS delete queue {queue}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL dipc queue test: {ex.GetType().Name}: {ex.Message}");
        try
        {
            if ((await client.GetIpcQueuesAsync()).Contains(queue))
            {
                await client.DeleteIpcQueueAsync(queue);
            }
        }
        catch
        {
            // 清理失败时忽略
        }
    }
}

async Task TestBackupAsync()
{
    await Try("backup/restore state", () => client.GetBackupStateAsync());
}

async Task TestControllerAsync()
{
    await Try("controller identity", () => client.GetControllerIdentityAsync());
    await Try("set controller name to current", async () =>
    {
        var identity = await client.GetControllerIdentityAsync();
        await client.SetControllerNameAsync(identity.Name);
        return identity.Name;
    });
    await Try("certificate stores", async () =>
        string.Join(", ", await client.GetCertificateStoresAsync()));
    await Try("trust CA certificates", async () =>
        (await client.GetCertificatesAsync("controller/trust_ca_store")).Count);
    await Try("safety mode", () => client.GetSafetyModeAsync());
    await Try("safety brake check", () => client.GetSafetyBrakeCheckAsync());
    await Try("safety load", () => client.GetSafetyLoadStatusAsync());
    await Try("safety configuration", () => client.GetSafetyConfigurationAsync());
    await Try("safety violation", async () =>
    {
        var violation = await client.GetSafetyViolationAsync();
        return $"violation={violation.Values.GetValueOrDefault("violation-num")} " +
               $"type={violation.Values.GetValueOrDefault("violation-type")}";
    });
    await Try("registry entries", async () =>
        string.Join(", ", await client.GetRegistryEntriesAsync()));
    await Try("registry rw-services", async () =>
        (await client.GetRegistryContentAsync("rw-services")).Length);
    await Try("controller options", async () =>
        (await client.GetControllerOptionsAsync()).Count);
    await Try("controller features", async () =>
        (await client.GetControllerFeaturesAsync()).Count);
}

async Task PrintStatusAsync()
{
    var speed = await client.GetSpeedRatioAsync();
    var pointer = await client.GetProgramPointerAsync();
    var execution = await client.GetExecutionStateAsync();
    var ctrl = await client.GetControllerStateAsync();
    var opmode = await client.GetOperationModeAsync();
    var tcp = await client.GetRobTargetAsync();
    Console.WriteLine($"状态: {ctrl}/{opmode} 执行={execution.CtrlExecState}/{execution.Cycle} " +
                      $"速度={speed}% PP={pointer.ModuleName}/{pointer.RoutineName}");
    Console.WriteLine($"TCP: X={tcp.TransMm[0]:F3} Y={tcp.TransMm[1]:F3} Z={tcp.TransMm[2]:F3}");
    try
    {
        var modules = await client.GetRapidModulesAsync();
        Console.WriteLine($"临时模块已加载: {modules.Any(m => m.Name == ModuleName)}");
    }
    catch
    {
        // 忽略
    }
}

sealed record SavedState(
    string Module,
    string Routine,
    double SpeedRatio,
    double[] Tcp,
    double[] RotQuaternion,
    DateTimeOffset SavedAt);
