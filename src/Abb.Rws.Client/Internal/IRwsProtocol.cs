using System.Net;

namespace Abb.Rws.Client.Internal;

internal interface IRwsProtocol : IDisposable
{
    RwsProtocolVersion Version { get; }

    Uri BaseUri { get; }

    /// <summary>最近一次请求使用的 RWS 内容版本，如 2.0 / 2.1；RWS 1.0 无此概念。</summary>
    string? ApiVersion { get; }

    CookieContainer Cookies { get; }

    /// <summary>为 WebSocket 握手生成认证头；不需要时返回 null。</summary>
    string? CreateWebSocketAuthorization(Uri uri);

    Task<RwsResult> GetAsync(string resource, CancellationToken ct = default);

    Task<RwsResult> GetAsync(string resource, string? accept, CancellationToken ct = default);

    Task<RwsOptionsResult> OptionsAsync(string resource, CancellationToken ct = default);

    Task<RwsResult> PostAsync(string resource, IReadOnlyDictionary<string, string>? form, CancellationToken ct = default);

    Task<RwsResult> PostRawAsync(string resource, string body, CancellationToken ct = default);

    Task<RwsResult> PutFormAsync(string resource, IReadOnlyDictionary<string, string> form,
        CancellationToken ct = default);

    Task<RwsFileMetadata> HeadAsync(string resource, CancellationToken ct = default);

    Task<byte[]> GetBytesAsync(string resource, CancellationToken ct = default);

    Task<byte[]> GetBytesAsync(string resource, string? accept, CancellationToken ct = default);

    Task PutBytesAsync(string resource, byte[] data, CancellationToken ct = default);

    Task DeleteAsync(string resource, CancellationToken ct = default);
}
