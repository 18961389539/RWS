using System.Text;
using Abb.Rws.Client.Internal;

namespace Abb.Rws.Client;

public sealed partial class RwsClient
{
    /// <summary>
    /// 创建 WebSocket 订阅。返回的对象通过 <see cref="RwsSubscription.Events"/> 推送事件。
    /// </summary>
    public async Task<RwsSubscription> SubscribeAsync(IReadOnlyList<RwsSubscriptionResource> resources,
        CancellationToken ct = default)
    {
        if (resources.Count == 0)
        {
            throw new ArgumentException("至少需要一个订阅资源", nameof(resources));
        }

        // RWS 要求先列出全部 resources 索引，再给出索引对应的资源路径。
        var body = new StringBuilder();
        for (var i = 0; i < resources.Count; i++)
        {
            body.Append("resources=").Append(i + 1).Append('&');
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var index = i + 1;
            body.Append(index).Append('=').Append(Uri.EscapeDataString(resources[i].ResourcePath))
                .Append('&').Append(index).Append("-p=").Append(resources[i].Priority)
                .Append('&');
        }

        var result = await _protocol.PostRawAsync("subscription", body.ToString().TrimEnd('&'), ct)
            .ConfigureAwait(false);

        var initialEvents = result.Items.Select(ToEvent).ToList();
        var wsUrl = result.Location
                    ?? FindFirst(result.RawBody, @"(wss?://[^""<\s]+)")
                    ?? (result.SelfHref?.StartsWith("ws", StringComparison.OrdinalIgnoreCase) == true
                        ? result.SelfHref
                        : null);
        if (string.IsNullOrEmpty(wsUrl))
        {
            throw new RwsException("订阅创建成功，但响应中没有 WebSocket 地址");
        }

        var groupPath = FindFirst(result.RawBody, @"href=""(subscription/\d+)""");
        if (groupPath is null)
        {
            var id = FindFirst(wsUrl, @"/poll/(\d+)");
            if (id is not null)
            {
                groupPath = $"subscription/{id}";
            }
        }

        try
        {
            return await RwsSubscription.ConnectAsync(_protocol, wsUrl, groupPath, initialEvents,
                _verifyCertificate, ct).ConfigureAwait(false);
        }
        catch
        {
            // 连接失败时清理已创建但无人使用的订阅组，否则控制器的订阅服务会被占用。
            if (groupPath is not null)
            {
                try
                {
                    await _protocol.DeleteAsync(groupPath, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // 控制器可能已自行清理
                }
            }

            throw;
        }
    }

    /// <summary>列出当前客户端的活动订阅组。</summary>
    public async Task<IReadOnlyList<RwsSubscriptionGroup>> GetSubscriptionGroupsAsync(
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync("subscription", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "subsgrp-li")
            .Select(item => new RwsSubscriptionGroup(ParseInt(item.Title), item.EventsHref))
            .ToList();
    }

    /// <summary>读取订阅组内的资源列表。</summary>
    public async Task<IReadOnlyList<RwsSubscriptionEntry>> GetSubscriptionGroupAsync(int groupId,
        CancellationToken ct = default)
    {
        var result = await _protocol.GetAsync($"subscription/{groupId}", ct).ConfigureAwait(false);
        return result.Items
            .Where(item => item.Type == "sub-resource")
            .Select(item => new RwsSubscriptionEntry(ParseInt(item.Title), item.Href ?? string.Empty))
            .ToList();
    }

    /// <summary>向活动订阅组追加一个资源，返回该资源在组内的索引。</summary>
    public async Task<RwsSubscriptionEntry> AddSubscriptionResourceAsync(int groupId,
        RwsSubscriptionResource resource, CancellationToken ct = default)
    {
        var entries = await GetSubscriptionGroupAsync(groupId, ct).ConfigureAwait(false);
        var index = entries.Count == 0 ? 1 : entries.Max(entry => entry.Index) + 1;
        var form = new Dictionary<string, string>
        {
            ["resources"] = index.ToString(),
            [index.ToString()] = resource.ResourcePath,
            [$"{index}-p"] = resource.Priority.ToString()
        };
        await _protocol.PutFormAsync($"subscription/{groupId}", form, ct).ConfigureAwait(false);

        // 控制器可能重排索引，重新读取以返回实际条目。
        var updated = await GetSubscriptionGroupAsync(groupId, ct).ConfigureAwait(false);
        return updated.FirstOrDefault(entry =>
                   entry.ResourcePath.Equals(resource.ResourcePath, StringComparison.OrdinalIgnoreCase))
               ?? new RwsSubscriptionEntry(index, resource.ResourcePath);
    }

    /// <summary>修改订阅组内某个资源的优先级。</summary>
    public async Task SetSubscriptionResourcePriorityAsync(int groupId, int resourceIndex, int priority,
        CancellationToken ct = default)
    {
        var entries = await GetSubscriptionGroupAsync(groupId, ct).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(item => item.Index == resourceIndex)
                    ?? throw new ArgumentException($"订阅组 {groupId} 中没有索引 {resourceIndex}", nameof(resourceIndex));
        var form = new Dictionary<string, string>
        {
            ["resources"] = resourceIndex.ToString(),
            [resourceIndex.ToString()] = entry.ResourcePath,
            [$"{resourceIndex}-p"] = priority.ToString()
        };
        await _protocol.PutFormAsync($"subscription/{groupId}", form, ct).ConfigureAwait(false);
    }

    /// <summary>从订阅组移除一个资源。</summary>
    public Task RemoveSubscriptionResourceAsync(int groupId, string resourcePath,
        CancellationToken ct = default)
    {
        var path = $"subscription/{groupId}/{resourcePath.TrimStart('/')}";
        return _protocol.DeleteAsync(path, ct);
    }

    /// <summary>删除订阅组。</summary>
    public Task DeleteSubscriptionGroupAsync(int groupId, CancellationToken ct = default) =>
        _protocol.DeleteAsync($"subscription/{groupId}", ct);

    internal static RwsEvent ToEvent(RwsItem item) => new(
        item.Type ?? string.Empty,
        item.Title,
        item.Href,
        new Dictionary<string, string>(item.Values, StringComparer.OrdinalIgnoreCase));

    private static string? FindFirst(string? text, string pattern)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }
}
