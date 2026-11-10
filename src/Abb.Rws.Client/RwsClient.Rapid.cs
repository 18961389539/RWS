using System.Globalization;

namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>列出任务中的 RAPID 模块。</summary>
    public async Task<IReadOnlyList<RapidModuleInfo>> GetRapidModulesAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/modules"
            : $"rw/rapid/modules?task={task}";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Value("name") is not null || item.Value("modname") is not null)
            .Select(ParseModule)
            .ToList();
    }

    /// <summary>读取单个模块的详细信息。</summary>
    public async Task<RapidModuleInfo> GetRapidModuleAsync(string module, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/modules/{module}"
            : $"rw/rapid/modules/{module}?task={task}";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        var item = result.Items.LastOrDefault(i => i.Value("modname") is not null || i.Value("filename") is not null)
                   ?? result.First();
        return ParseModule(item);
    }

    /// <summary>读取模块源代码文本。</summary>
    public async Task<string> GetRapidModuleTextAsync(string module, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/modules/{module}/text"
            : $"rw/rapid/modules/{module}?task={task}&resource=module-text";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Value("module-text") is not null) ?? result.First();
        return item.Value("module-text") ?? item.Value("text") ?? string.Empty;
    }

    /// <summary>列出模块中的例行程序（PROC/FUNC/TRAP）。</summary>
    public async Task<IReadOnlyList<string>> GetRapidModuleRoutinesAsync(string module, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["view"] = "block",
            ["vartyp"] = "any",
            ["blockurl"] = $"RAPID/{task}/{module}",
            ["symtyp"] = "any",
            ["recursive"] = "FALSE",
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
        return result.Items
            .Where(item => item.Value("symtyp") is "prc" or "fun" or "trp")
            .Select(item => item.Value("name") ?? item.Title ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>读取程序指针。</summary>
    public async Task<ProgramPointerInfo> GetProgramPointerAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"rw/rapid/tasks/{task}/pcp", ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Title == "progpointer") ?? result.First();
        return new ProgramPointerInfo(
            item.Value("modulename") ?? item.Value("modulemame"),
            item.Value("routinename"),
            item.Value("beginposition") ?? item.Value("begposition"),
            item.Value("endposition"),
            ParseUInt(item.Value("changecount")));
    }

    /// <summary>读取程序的构建/语法错误。</summary>
    public async Task<IReadOnlyList<RapidBuildError>> GetBuildErrorsAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var result = await _protocol
            .GetAsync($"rw/rapid/tasks/{task}/program/builderror", ct).ConfigureAwait(false);
        return result.Items.Select(item => new RapidBuildError(
            item.Value("module-name") ?? item.Value("modulename") ?? item.Value("module") ?? string.Empty,
            ParseInt(item.Value("row")),
            ParseInt(item.Value("column") ?? item.Value("col")),
            item.Value("error") ?? item.Value("text") ?? string.Empty)).ToList();
    }

    /// <summary>按 URL 执行例行程序（RWS 2.0）。</summary>
    public Task ExecuteRoutineAsync(string module, string routine, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("routine-from-url 仅在 RWS 2.0 上受支持");
        }

        var form = new Dictionary<string, string>
        {
            ["routineurl"] = $"/RAPID/{task}/{module}/{routine}",
            ["userlevel"] = "false"
        };
        return _protocol.PostAsync($"rw/rapid/tasks/{task}/pcp/routine-from-url", form, ct);
    }

    /// <summary>激活任务。</summary>
    public Task ActivateTaskAsync(string task, CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/activate"
            : $"rw/rapid/tasks/{task}?action=activate";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>停用任务。</summary>
    public Task DeactivateTaskAsync(string task, CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/deactivate"
            : $"rw/rapid/tasks/{task}?action=deactivate";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>加载单个模块。需要 Edit 控制权。</summary>
    public Task LoadModuleAsync(string modulePath, bool replace = false, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        if (Version == RwsProtocolVersion.Rws2 && modulePath.StartsWith('$'))
        {
            modulePath = modulePath[1..];
        }

        var form = new Dictionary<string, string>
        {
            ["modulepath"] = modulePath,
            ["replace"] = replace ? "true" : "false"
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/loadmod"
            : $"rw/rapid/tasks/{task}?action=loadmod";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>卸载模块。需要 Edit 控制权。</summary>
    public Task UnloadModuleAsync(string module, string task = "T_ROB1", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["module"] = module };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/unloadmod"
            : $"rw/rapid/tasks/{task}?action=unloadmod";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>保存模块到控制器磁盘。需要 Edit 控制权。</summary>
    public Task SaveModuleAsync(string module, string path, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["name"] = module,
            ["path"] = path
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/modules/{module}/save"
            : $"rw/rapid/modules/{module}?task={task}&action=save";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>替换模块源代码。需要 Edit 控制权。</summary>
    public Task SetModuleTextAsync(string module, string text, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["task"] = task,
            ["text"] = text
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/modules/{module}/text"
            : $"rw/rapid/modules/{module}?task={task}&action=set-module-text";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>加载 RAPID 程序文件。需要 Edit 控制权。</summary>
    public Task LoadProgramAsync(string programPath, string loadMode = "replace", string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["progpath"] = programPath,
            ["loadmode"] = loadMode
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/program/load"
            : $"rw/rapid/tasks/{task}/program?action=loadprog";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>卸载任务程序。需要 Edit 控制权。</summary>
    public Task UnloadProgramAsync(string task = "T_ROB1", CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/program/unload"
            : $"rw/rapid/tasks/{task}/program?action=unloadprog";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>保存任务程序。需要 Edit 控制权。</summary>
    public Task SaveProgramAsync(string path, string task = "T_ROB1", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["path"] = path };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/program/save"
            : $"rw/rapid/tasks/{task}/program?action=save";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>重置任务程序指针（RWS 2.0）。</summary>
    public Task ResetTaskProgramPointerAsync(string task = "T_ROB1", CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("任务级程序指针重置仅在 RWS 2.0 上受支持");
        }

        return _protocol.PostAsync($"rw/rapid/tasks/{task}/pcp/reset", null, ct);
    }

    private static RapidModuleInfo ParseModule(Internal.RwsItem item)
    {
        var name = item.Value("name") ?? item.Value("modname") ?? item.Title ?? string.Empty;
        if (name.Contains('/'))
        {
            name = name[(name.LastIndexOf('/') + 1)..];
        }

        return new RapidModuleInfo(
            name,
            item.Value("type") ?? item.Value("attribute") ?? string.Empty,
            item.Value("filename"),
            item.Value("attribute"));
    }

    private static uint ParseUInt(string? text) =>
        uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
