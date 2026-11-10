using System.Xml.Linq;

namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>列出 CFG 域（EIO/MOC/PROC/SIO/SYS 等）。</summary>
    public async Task<IReadOnlyList<string>> GetCfgDomainsAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("rw/cfg", ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Title ?? item.Value("name") ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>列出域下的配置类型。</summary>
    public async Task<IReadOnlyList<string>> GetCfgTypesAsync(string domain, CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"rw/cfg/{domain}", ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Title ?? item.Value("name") ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>读取配置类型的属性数和实例数。</summary>
    public async Task<CfgTypeInfo> GetCfgTypeAsync(string domain, string type, CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"rw/cfg/{domain}/{type}", ct).ConfigureAwait(false);
        var item = result.First();
        return new CfgTypeInfo(
            type,
            ParseInt(item.Value("cfg-no-of-attributes")),
            ParseInt(item.Value("cfg-no-of-instances")));
    }

    /// <summary>列出配置实例名称。</summary>
    public async Task<IReadOnlyList<string>> GetCfgInstancesAsync(string domain, string type,
        CancellationToken ct = default)
    {
        var result = await _protocol
            .GetAsync($"rw/cfg/{domain}/{type}/instances", ct).ConfigureAwait(false);
        return result.Items.Select(item => item.Title ?? item.Value("name") ?? string.Empty)
            .Where(name => name.Length > 0).ToList();
    }

    /// <summary>读取配置实例的属性值。</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetCfgInstanceAsync(string domain, string type,
        string instance, CancellationToken ct = default)
    {
        if (Version == RwsProtocolVersion.Rws1)
        {
            // RWS 1.0 没有单实例资源，读取列表后按 _title 过滤
            var list = await _protocol.GetAsync($"rw/cfg/{domain}/{type}/instances", ct)
                .ConfigureAwait(false);
            var entry = list.Items.FirstOrDefault(i => i.Title == instance) ?? list.First();
            return entry.Values
                .Where(pair => !pair.Key.StartsWith('_') && pair.Key is not "rdonly" and not "instanceid")
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        var result = await _protocol
            .GetAsync($"rw/cfg/{domain}/{type}/instances/{instance}", ct).ConfigureAwait(false);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(result.RawBody))
        {
            try
            {
                var document = XDocument.Parse(result.RawBody);
                foreach (var li in document.Descendants()
                             .Where(e => e.Name.LocalName == "li" &&
                                         e.Attribute("class")?.Value is "cfg-ia-t" or "cfg-ia"))
                {
                    var name = li.Attribute("title")?.Value;
                    var value = li.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "span" &&
                                             e.Attribute("class")?.Value == "value")?.Value;
                    if (!string.IsNullOrEmpty(name) && value is not null)
                    {
                        values[name] = value.Trim();
                    }
                }
            }
            catch
            {
                // 退回通用解析
            }
        }

        if (values.Count == 0 && result.Items.Count > 0)
        {
            foreach (var pair in result.First().Values)
            {
                if (!pair.Key.StartsWith('_') && pair.Key is not "rdonly" and not "instanceid")
                {
                    values[pair.Key] = pair.Value;
                }
            }
        }

        return values;
    }

    /// <summary>更新配置实例属性。需要 Edit 控制权。</summary>
    public Task SetCfgInstanceAsync(string domain, string type, string instance,
        IReadOnlyDictionary<string, string> attributes, CancellationToken ct = default)
    {
        var resource = $"rw/cfg/{domain}/{type}/instances/{instance}";
        if (Version == RwsProtocolVersion.Rws2)
        {
            var body = string.Join('&', attributes.Select(pair => $"{pair.Key}=[{pair.Value},1]"));
            return _protocol.PostRawAsync(resource, body, ct);
        }

        return _protocol.PostAsync($"{resource}?action=set", attributes, ct);
    }

    /// <summary>创建配置实例（使用默认属性）。需要 Edit 控制权。</summary>
    public Task CreateCfgInstanceAsync(string domain, string type, string instance,
        CancellationToken ct = default)
    {
        if (Version == RwsProtocolVersion.Rws2)
        {
            return _protocol.PostRawAsync($"rw/cfg/{domain}/{type}/instances/create-default",
                $"name={instance}", ct);
        }

        var form = new Dictionary<string, string> { ["name"] = instance };
        return _protocol.PostAsync($"rw/cfg/{domain}/{type}/instances?action=create-default", form, ct);
    }

    /// <summary>删除配置实例。需要 Edit 控制权。</summary>
    public Task DeleteCfgInstanceAsync(string domain, string type, string instance,
        CancellationToken ct = default)
    {
        return _protocol.DeleteAsync($"rw/cfg/{domain}/{type}/instances/{instance}", ct);
    }

    /// <summary>保存整个 CFG 域到文件。</summary>
    public Task SaveCfgDomainAsync(string domain, string filePath, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["filepath"] = filePath };
        var resource = Version == RwsProtocolVersion.Rws2
            ? $"rw/cfg/{domain}/saveas"
            : $"rw/cfg/{domain}?action=saveas";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>加载 CFG 文件。</summary>
    public Task LoadCfgFileAsync(string filePath, string action = "replace", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["filepath"] = filePath,
            ["action-type"] = action
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/cfg/load"
            : "rw/cfg?action=load";
        return _protocol.PostAsync(resource, form, ct);
    }

    /// <summary>校验 CFG 文件（不写入）。</summary>
    public Task ValidateCfgFileAsync(string filePath, string action = "add", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["filepath"] = filePath,
            ["action-type"] = action
        };
        var resource = Version == RwsProtocolVersion.Rws2
            ? "rw/cfg/validate"
            : "rw/cfg?action=validate";
        return _protocol.PostAsync(resource, form, ct);
    }
}
