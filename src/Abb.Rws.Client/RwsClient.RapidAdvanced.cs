namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    // ── 断点 ──────────────────────────────────────────────────────────────

    /// <summary>列出任务中的断点。</summary>
    public async Task<IReadOnlyList<RapidBreakpoint>> GetBreakpointsAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/program/breakpoints"
            : $"rw/rapid/tasks/{task}/program/breakpoint";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.Items.Select(item => new RapidBreakpoint(
            item.Value("module-name") ?? item.Value("module") ?? item.Title ?? string.Empty,
            ParseInt(item.Value("start-row") ?? item.Value("row")),
            ParseInt(item.Value("start-col") ?? item.Value("column") ?? item.Value("col")))).ToList();
    }

    /// <summary>设置断点。需要 Edit 控制权。</summary>
    public Task SetBreakpointAsync(string module, int row, int column, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["module"] = module,
            ["row"] = row.ToString(),
            ["column"] = column.ToString()
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/program/breakpoints"
            : $"rw/rapid/tasks/{task}/program/breakpoint?action=set";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>删除断点；all=true 时清空任务的全部断点（RWS 2.0）。</summary>
    public Task RemoveBreakpointAsync(string? module = null, int? row = null, int? column = null,
        bool all = false, string task = "T_ROB1", CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("断点删除仅在 RWS 2.0 上提供");
        }

        var form = new Dictionary<string, string>();
        if (all)
        {
            form["all"] = "true";
        }
        else
        {
            form["module"] = module ?? throw new ArgumentNullException(nameof(module));
            form["row"] = (row ?? throw new ArgumentNullException(nameof(row))).ToString();
            form["column"] = (column ?? throw new ArgumentNullException(nameof(column))).ToString();
        }

        return _protocol.PostAsync($"rw/rapid/tasks/{task}/program/breakpoints/remove", form, ct);
    }

    // ── 调用栈 / 指针 / 计数 ──────────────────────────────────────────────

    /// <summary>读取任务当前调用栈帧。</summary>
    public async Task<ActivationRecord?> GetActivationRecordAsync(string task = "T_ROB1", int stackFrame = 1,
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/activation-record"
            : $"rw/rapid/tasks/{task}?resource=activation-record&stackframe={stackFrame}";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Title == "stackframe") ?? result.Items.FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        return new ActivationRecord(
            item.Value("execlevel") ?? string.Empty,
            ParseInt(item.Value("beg-row")),
            ParseInt(item.Value("beg-col")),
            ParseInt(item.Value("end-row")),
            ParseInt(item.Value("end-col")),
            item.Value("stack-url"),
            item.Value("routine-url"));
    }

    /// <summary>读取运动指针；没有启动过程序时位置可能为空。</summary>
    public async Task<ProgramPointerInfo> GetMotionPointerAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"rw/rapid/tasks/{task}/pcp", ct).ConfigureAwait(false);
        var item = result.Items.FirstOrDefault(i => i.Title == "motionpointer");
        if (item is null || item.Values.Count == 0)
        {
            return new ProgramPointerInfo(null, null, null, null, 0);
        }

        return new ProgramPointerInfo(
            item.Value("modulename") ?? item.Value("modulemame"),
            item.Value("routinename"),
            item.Value("beginposition") ?? item.Value("begposition"),
            item.Value("endposition"),
            ParseUInt(item.Value("changecount")));
    }

    /// <summary>读取程序指针同步状态（RWS 2.0）。</summary>
    public async Task<string> GetProgramPointerSyncStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/rapid/tasks/syncstate/program-pointer", ct)
            .ConfigureAwait(false)).First();
        return item.Value("program-pointer-state") ?? string.Empty;
    }

    /// <summary>读取运动指针同步状态（RWS 2.0）。</summary>
    public async Task<string> GetMotionPointerSyncStateAsync(CancellationToken ct = default)
    {
        var item = (await _protocol.GetAsync("rw/rapid/tasks/syncstate/motion-pointer", ct)
            .ConfigureAwait(false)).First();
        return item.Value("motion-pointer-state") ?? string.Empty;
    }

    /// <summary>读取任务结构变更计数。</summary>
    public async Task<TaskStructuralChangeCount> GetStructuralChangeCountAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/structural-changecount"
            : $"rw/rapid/tasks/{task}?resource=task-struc-change-count";
        var item = (await _protocol.GetAsync(resource, ct).ConfigureAwait(false)).First();
        return new TaskStructuralChangeCount(
            ParseLong(item.Value("change-count")),
            ParseLong(item.Value("struc-change-count")));
    }

    /// <summary>读取任务选择状态。</summary>
    public async Task<IReadOnlyList<TaskSelectionInfo>> GetTaskSelectionAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/rapid/taskselection", ct).ConfigureAwait(false);
        return result.Items.Select(item => new TaskSelectionInfo(
            item.Value("name") ?? item.Title ?? string.Empty,
            item.Value("state") ?? string.Empty,
            IsTrue(item.Value("motiontask")),
            IsTrue(item.Value("usermodify")))).ToList();
    }

    /// <summary>列出服务例行程序。</summary>
    public async Task<IReadOnlyList<RapidServiceRoutine>> GetServiceRoutinesAsync(string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"rw/rapid/tasks/{task}/serviceroutine", ct).ConfigureAwait(false);
        return result.Items.Select(item => new RapidServiceRoutine(
            item.Value("routine_name") ?? item.Value("routine-name") ?? item.Title ?? string.Empty,
            item.Value("url-to-routine") ?? item.Value("url_to_routine") ?? string.Empty,
            IsTrue(item.Value("service-routine")))).ToList();
    }

    /// <summary>读取当前活动的 UI 指令；没有活动指令时返回 null。</summary>
    public async Task<RapidUiInstruction?> GetActiveUiInstructionAsync(CancellationToken ct = default)
    {
        Internal.RwsResult result;
        try
        {
            result = await _protocol.GetAsync("rw/rapid/uiinstr/active", ct).ConfigureAwait(false);
        }
        catch (RwsException ex) when (ex.HttpStatusCode == 404)
        {
            return null;
        }

        var item = result.Items.FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        return new RapidUiInstruction(item.Title, item.Href,
            new Dictionary<string, string>(item.Values, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>读取模块文本的指定行/列范围。</summary>
    public async Task<string> GetModuleTextRangeAsync(string module, int startRow, int startCol, int endRow,
        int endCol, string task = "T_ROB1", CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/modules/{module}/text/range" +
              $"?startrow={startRow}&startcol={startCol}&endrow={endRow}&endcol={endCol}"
            : $"rw/rapid/modules/{module}?task={task}" +
              $"&startrow={startRow}&startcol={startCol}&endrow={endRow}&endcol={endCol}";
        var result = await _protocol.GetAsync(resource, ct).ConfigureAwait(false);
        return result.First().Value("text") ?? string.Empty;
    }

    /// <summary>读取 RAPID 执行错误状态（RobotWare 8.2+；RW7 返回 null）。</summary>
    public async Task<bool?> GetExecutionErrorStateAsync(CancellationToken ct = default)
    {
        Internal.RwsResult result;
        try
        {
            result = await _protocol.GetAsync("rw/rapid/execution/execerrorstate", ct).ConfigureAwait(false);
        }
        catch (RwsException ex) when (ex.HttpStatusCode == 404)
        {
            return null;
        }

        var value = result.First().Value("execerrorstate");
        return value is not null &&
               (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
    }

    // ── UI 指令 / Hold-to-run / 程序指针 ───────────────────────────────────

    /// <summary>回答活动 UI 指令的参数。</summary>
    public Task SetUiInstructionParamAsync(string stackUrl, string uiParam, string value,
        CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/uiinstr/active/param/{Escape(stackUrl)}/{Escape(uiParam)}"
            : $"rw/rapid/uiinstr/active/param/{Escape(stackUrl)}/{Escape(uiParam)}?action=set";
        var form = new Dictionary<string, string> { ["value"] = value };
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>设置 Hold-to-run 状态。</summary>
    public Task HoldToRunAsync(bool enabled, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["state"] = enabled ? "true" : "false" };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/rapid/execution/holdtorun"
            : "rw/rapid/execution?action=holdtorun-state";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>设置程序指针到例程。需要 Edit 控制权。</summary>
    public Task SetProgramPointerAsync(string module, string routine, bool userLevel = false,
        string task = "T_ROB1", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["module"] = module,
            ["routine"] = routine
        };
        if (Version == RwsProtocolVersion.Rws2)
        {
            form["userlevel"] = userLevel ? "true" : "false";
            return _protocol.PostAsync($"rw/rapid/tasks/{task}/pcp/routine", form, ct);
        }

        return _protocol.PostAsync($"rw/rapid/tasks/{task}/pcp?action=set-pp-routine", form, ct);
    }

    /// <summary>设置程序指针到模块光标位置。</summary>
    public Task SetProgramPointerToCursorAsync(string module, int line, int column, string? routine = null,
        string task = "T_ROB1", CancellationToken ct = default)
    {
        if (Version == RwsProtocolVersion.Rws2)
        {
            var form = new Dictionary<string, string>
            {
                ["module"] = module,
                ["line"] = line.ToString(),
                ["column"] = column.ToString()
            };
            return _protocol.PostAsync($"rw/rapid/tasks/{task}/pcp/cursor", form, ct);
        }

        if (string.IsNullOrEmpty(routine))
        {
            throw new ArgumentException("RWS 1.0 设置光标位置需要 routine", nameof(routine));
        }

        var rws1Form = new Dictionary<string, string>
        {
            ["module"] = module,
            ["line"] = line.ToString(),
            ["column"] = column.ToString(),
            ["routine"] = routine
        };
        return _protocol.PostAsync($"rw/rapid/tasks/{task}/pcp?action=set-pp-cursor", rws1Form, ct);
    }

    /// <summary>程序指针上一条指令（手动模式）。</summary>
    public Task PpPreviousInstructionAsync(string task = "T_ROB1", CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/pcp/prev-inst"
            : $"rw/rapid/tasks/{task}/pcp?action=set-pp-prev-inst";
        return _protocol.PostAsync(resource, null, ct);
    }

    /// <summary>程序指针下一条指令（手动模式）。</summary>
    public Task PpNextInstructionAsync(string task = "T_ROB1", CancellationToken ct = default)
    {
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/pcp/next-inst"
            : $"rw/rapid/tasks/{task}/pcp?action=set-pp-next-inst";
        return _protocol.PostAsync(resource, null, ct);
    }

    // ── 程序入口 / 程序名 / 模块文本范围 / ModPos ──────────────────────────

    /// <summary>设置程序入口例程。</summary>
    public Task SetProgramEntryPointAsync(string routine, string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["routine"] = routine };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/program/entrypoint"
            : $"rw/rapid/tasks/{task}/program?action=set-entrypoint";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>设置程序名称（RWS 2.0）。</summary>
    public Task SetProgramNameAsync(string name, string task = "T_ROB1", CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("设置程序名称仅在 RWS 2.0 上提供");
        }

        var form = new Dictionary<string, string> { ["name"] = name };
        return _protocol.PostAsync($"rw/rapid/tasks/{task}/program/name", form, ct);
    }

    /// <summary>替换模块文本的指定范围。replaceMode/queryMode 需要按控制器表单填写。</summary>
    public Task SetModuleTextRangeAsync(string module, int startRow, int startCol, int endRow, int endCol,
        string text, string replaceMode = "replace", string queryMode = "1", string task = "T_ROB1",
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["task"] = task,
            ["startrow"] = startRow.ToString(),
            ["startcol"] = startCol.ToString(),
            ["endrow"] = endRow.ToString(),
            ["endcol"] = endCol.ToString(),
            ["text"] = text
        };
        if (!string.IsNullOrEmpty(replaceMode))
        {
            form["replace-mode"] = replaceMode;
        }

        if (!string.IsNullOrEmpty(queryMode))
        {
            form["query-mode"] = queryMode;
        }

        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/rapid/tasks/{task}/modules/{module}/text/range"
            : $"rw/rapid/modules/{module}?task={task}&action=set-text-range";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>修改模块中的位置（ModPos，RWS 2.0）。</summary>
    public Task ModifyPositionAsync(string module, int startRow, int startCol, string newPositionText,
        int? endRow = null, int? endCol = null, bool checkLimit = true, bool checkDeactAxes = true,
        bool allowDeact = false, string task = "T_ROB1", CancellationToken ct = default)
    {
        if (Version != RwsProtocolVersion.Rws2)
        {
            throw new NotSupportedException("ModPos 仅在 RWS 2.0 上提供");
        }

        var form = new Dictionary<string, string>
        {
            ["startrow"] = startRow.ToString(),
            ["startcol"] = startCol.ToString(),
            ["endrow"] = (endRow ?? startRow).ToString(),
            ["endcol"] = (endCol ?? startCol).ToString(),
            ["checklimit"] = checkLimit ? "true" : "false",
            ["checkdeactaxes"] = checkDeactAxes ? "true" : "false",
            ["allowdeact"] = allowDeact ? "true" : "false",
            ["text"] = newPositionText
        };
        return _protocol.PostAsync($"rw/rapid/tasks/{task}/modules/{module}/modify-position", form, ct);
    }

    // ── 批量任务启停 ──────────────────────────────────────────────────────

    /// <summary>批量激活任务。</summary>
    public async Task ActivateTasksAsync(IEnumerable<string> tasks, CancellationToken ct = default)
    {
        foreach (var task in tasks)
        {
            await ActivateTaskAsync(task, ct).ConfigureAwait(false);
        }
    }

    /// <summary>批量停用任务。</summary>
    public async Task DeactivateTasksAsync(IEnumerable<string> tasks, CancellationToken ct = default)
    {
        foreach (var task in tasks)
        {
            await DeactivateTaskAsync(task, ct).ConfigureAwait(false);
        }
    }
}
