namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>列出文件服务卷。</summary>
    public async Task<IReadOnlyList<FileVolumeInfo>> ListFileVolumesAsync(CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("fileservice", ct).ConfigureAwait(false);
        return result.Items.Select(item => new FileVolumeInfo(
            item.Title ?? item.Value("name") ?? string.Empty,
            item.Type)).ToList();
    }

    /// <summary>读取文件元数据（长度等）。基于父目录列表，避免 fileservice HEAD 406。</summary>
    public async Task<RwsFileMetadata> GetFileMetadataAsync(string path, CancellationToken ct = default)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        if (slash <= 0 || slash == trimmed.Length - 1)
        {
            throw new ArgumentException("需要文件路径，例如 $HOME/file.txt", nameof(path));
        }

        var parent = trimmed[..slash];
        var name = trimmed[(slash + 1)..];
        var files = await ListFilesAsync(parent, ct).ConfigureAwait(false);
        var file = files.FirstOrDefault(f => f.Name == name);
        if (file is null)
        {
            throw new RwsException($"文件不存在：{path}");
        }

        return new RwsFileMetadata(file.Size, null, null, null);
    }

    /// <summary>在指定目录下创建子目录。</summary>
    public Task CreateDirectoryAsync(string parentPath, string name, CancellationToken ct = default)
    {
        var parent = "fileservice/" + EscapePath(parentPath);
        if (Version == RwsProtocolVersion.Rws2)
        {
            var form = new Dictionary<string, string> { ["fs-newname"] = name };
            return _protocol.PostAsync(parent + "/create", form, ct);
        }

        var rws1Form = new Dictionary<string, string>
        {
            ["fs-action"] = "create",
            ["fs-newname"] = name
        };
        return _protocol.PostAsync(parent, rws1Form, ct);
    }

    /// <summary>在同一目录内复制文件。</summary>
    public Task CopyFileAsync(string sourcePath, string newName, bool overwrite = false,
        CancellationToken ct = default)
    {
        var source = "fileservice/" + EscapePath(sourcePath);
        if (Version == RwsProtocolVersion.Rws2)
        {
            var form = new Dictionary<string, string>
            {
                ["fs-newname"] = newName,
                ["fs-overwrite"] = overwrite ? "true" : "false"
            };
            return _protocol.PostAsync(source + "/copy", form, ct);
        }

        var rws1Form = new Dictionary<string, string>
        {
            ["fs-action"] = "copy",
            ["fs-newname"] = newName
        };
        return _protocol.PostAsync(source, rws1Form, ct);
    }

    /// <summary>在同一目录内重命名文件。</summary>
    public Task RenameFileAsync(string path, string newName, CancellationToken ct = default)
    {
        var source = "fileservice/" + EscapePath(path);
        if (Version == RwsProtocolVersion.Rws2)
        {
            var form = new Dictionary<string, string> { ["fs-newname"] = newName };
            return _protocol.PostAsync(source + "/rename", form, ct);
        }

        var rws1Form = new Dictionary<string, string>
        {
            ["fs-action"] = "rename",
            ["fs-newname"] = newName
        };
        return _protocol.PostAsync(source, rws1Form, ct);
    }

    /// <summary>删除目录。DELETE 同时适用于文件与目录。</summary>
    public Task DeleteDirectoryAsync(string path, CancellationToken ct = default)
    {
        return _protocol.DeleteAsync("fileservice/" + EscapePath(path), ct);
    }
}
