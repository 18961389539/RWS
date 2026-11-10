using Abb.Rws.Client;

var host = args.Length > 0 ? args[0] : "192.168.4.10";
using var client = await RwsClient.ConnectAsync(new RwsClientOptions { Host = host });
Console.WriteLine($"连接成功：{client.Version}  {client.BaseUri}");
Console.WriteLine();

await Try("system info", () => client.GetSystemInfoAsync());
await Try("robot type", () => client.GetRobotTypeAsync());
await Try("options count", async () => (await client.GetOptionsAsync()).Count);
await Try("license", () => client.GetLicenseAsync());

await Try("controller state", () => client.GetControllerStateAsync());
await Try("operation mode", () => client.GetOperationModeAsync());
await Try("speed ratio", () => client.GetSpeedRatioAsync());
await Try("execution state", () => client.GetExecutionStateAsync());
await Try("tasks", async () => (await client.GetTasksAsync()).Count);
await Try("joint target", () => client.GetJointTargetAsync());
await Try("rob target", () => client.GetRobTargetAsync());
await Try("digital io SafetyEnable", () => client.GetSignalAsync("SafetyEnable", "SC_Feedback_Net", "SC_Feedback_Dev"));
await Try("io networks", async () => (await client.GetIoNetworksAsync()).Count);
await Try("io devices", async () => (await client.GetIoDevicesAsync()).Count);

await Try("cfg domains", async () => string.Join(",", await client.GetCfgDomainsAsync()));
await Try("cfg types SYS", async () => (await client.GetCfgTypesAsync("SYS")).Count);
await Try("cfg instances SYS/PRESENT_OPTIONS",
    async () => (await client.GetCfgInstancesAsync("SYS", "PRESENT_OPTIONS")).Count);
await Try("cfg instance SYS/PRESENT_OPTIONS/RemoteService", async () =>
    (await client.GetCfgInstanceAsync("SYS", "PRESENT_OPTIONS", "RemoteService")).Count);

await Try("rapid modules", async () => (await client.GetRapidModulesAsync()).Count);
await Try("rapid module Wizard_Data", () => client.GetRapidModuleAsync("Wizard_Data"));
await Try("rapid module text length", async () => (await client.GetRapidModuleTextAsync("Wizard_Data")).Length);
await Try("rapid module routines", async () => string.Join(",", await client.GetRapidModuleRoutinesAsync("Module1")));
await Try("program pointer", () => client.GetProgramPointerAsync());
await Try("build errors", async () => (await client.GetBuildErrorsAsync()).Count);
await Try("rapid symbols count", async () => (await client.GetRapidSymbolsAsync()).Count);
await Try("rapid variable", () => client.GetRapidVariableAsync("Wizard_Data/wi_forceoffset"));
await Try("rapid jointtarget var", () => client.GetRapidVariableJointTargetAsync("Module1/j_Home"));
await Try("rapid robtarget var", () => client.GetRapidVariableRobTargetAsync("Module1/pPick"));

await Try("ipc info", () => client.GetIpcInfoAsync());
await Try("event log", async () => (await client.GetEventLogAsync(0, 3)).Count);
await Try("mastership", () => client.GetMastershipStateAsync());
await Try("rmmp", () => client.GetRmmpStateAsync());
await Try("login info", () => client.GetLoginInfoAsync());
await Try("user grants", async () => (await client.GetUserGrantsAsync()).Count);
await Try("files count", async () => (await client.ListFilesAsync()).Count);
Console.WriteLine();

await TestIpcAsync(client);
await TestSubscriptionAsync(client);
await TestWritesAsync(client);

static async Task TestIpcAsync(RwsClient client)
{
    const string queue = "dotnet_probe";
    try
    {
        await client.CreateIpcQueueAsync(queue, 100, 100);
        Console.WriteLine("PASS create IPC queue");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARN create IPC queue: {ex.Message}");
    }

    try
    {
        await client.SendIpcMessageAsync(queue, "hello from C#", queue);
        var messages = await client.ReadIpcMessagesAsync(queue);
        Console.WriteLine(messages.Count > 0
            ? $"PASS IPC send/read: {messages[0].Data}"
            : "FAIL IPC send/read: 队列为空");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL IPC send/read: {ex.GetType().Name}: {ex.Message}");
    }
}

static async Task TestSubscriptionAsync(RwsClient client)
{
    try
    {
        await using var subscription = await client.SubscribeAsync(
            new[] { RwsSubscriptionResource.SpeedRatio() });
        Console.WriteLine($"PASS subscription: 初始事件 {subscription.InitialEvents.Count} 条");

        var current = await client.GetSpeedRatioAsync();
        var probe = current > 0 ? current - 1 : current + 1;

        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);
        try
        {
            await client.SetSpeedRatioAsync(probe);
            await client.SetSpeedRatioAsync(current);
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var received = false;
        try
        {
            while (true)
            {
                var evt = await subscription.Events.ReadAsync(timeout.Token);
                if (evt.Type.Contains("speedratio", StringComparison.OrdinalIgnoreCase))
                {
                    received = true;
                    Console.WriteLine($"PASS subscription event: {evt.Type}={evt.Value("speedratio")}");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 超时
        }

        if (!received)
        {
            Console.WriteLine("FAIL subscription event: 8 秒内没有收到速度事件");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL subscription: {ex.GetType().Name}: {ex.Message}");
    }
}

static async Task TestWritesAsync(RwsClient client)
{
    // 值保持原样，避免改变机器人状态
    try
    {
        var ratio = await client.GetSpeedRatioAsync();
        var variable = await client.GetRapidVariableAsync("Wizard_Data/wi_forceoffset");

        await client.RequestMastershipAsync(MastershipDomain.Edit);
        await client.RequestMastershipAsync(MastershipDomain.Motion);
        try
        {
            await client.SetSpeedRatioAsync(ratio);
            Console.WriteLine($"PASS write speed ratio (保持 {ratio})");
            await client.SetRapidVariableAsync("Wizard_Data/wi_forceoffset", variable);
            Console.WriteLine($"PASS write RAPID variable (保持 {variable})");
        }
        finally
        {
            await client.ReleaseMastershipAsync(MastershipDomain.Motion);
            await client.ReleaseMastershipAsync(MastershipDomain.Edit);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL write test: {ex.GetType().Name}: {ex.Message}");
    }
}

static async Task Try<T>(string name, Func<Task<T>> action)
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
    JointTarget joint => $"RobAx=[{Join(joint.RobAxDeg)}] ExtAx=[{Join(joint.ExtAxDeg)}]",
    RobTarget target =>
        $"XYZ=[{Join(target.TransMm)}] Q=[{Join(target.RotQuaternion)}] Conf=[{Join(target.RobConf)}]",
    RwsSystemInfo system =>
        $"{system.Name} RobotWare {system.RobotWareVersion} ({system.Type})",
    System.Collections.IEnumerable items => string.Join(", ", items.Cast<object?>().Select(x => x?.ToString())),
    _ => value.ToString() ?? string.Empty
};

static string Join(double[] values) => string.Join(", ", values.Select(x => x.ToString("F4")));
