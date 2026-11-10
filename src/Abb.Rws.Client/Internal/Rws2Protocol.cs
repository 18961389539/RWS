using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Abb.Rws.Client.Internal;

/// <summary>
/// RWS 2.0：HTTPS + Basic 认证，响应为 XHTML。
/// </summary>
internal sealed class Rws2Protocol : IRwsProtocol
{
    private const string AcceptHeader = "application/xhtml+xml;v=2.0";
    private const string FormContentType = "application/x-www-form-urlencoded;v=2.0";

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();

    public Rws2Protocol(Uri baseUri, string username, string password, bool verifyCertificate, TimeSpan timeout)
    {
        BaseUri = baseUri;
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true
        };
        if (!verifyCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;
        }

        _http = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = timeout
        };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Basic " + credentials);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", AcceptHeader);
    }

    public RwsProtocolVersion Version => RwsProtocolVersion.Rws2;

    public Uri BaseUri { get; }

    public string? ApiVersion { get; private set; } = "2.0";

    public CookieContainer Cookies => _cookies;

    public string? CreateWebSocketAuthorization(Uri uri) => null;

    public Task<RwsResult> GetAsync(string resource, CancellationToken ct = default) =>
        GetAsync(resource, null, ct);

    public async Task<RwsResult> GetAsync(string resource, string? accept, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(resource));
        ApiVersion = ExtractVersion(accept) ?? "2.0";
        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        return await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<RwsOptionsResult> OptionsAsync(string resource, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, Url(resource));
        ApiVersion = "2.0";
        request.Headers.TryAddWithoutValidation("Accept", AcceptHeader);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateError(response.StatusCode, body);
        }

        var allow = new List<string>();
        if (response.Content.Headers.TryGetValues("Allow", out var contentAllow))
        {
            allow.AddRange(contentAllow);
        }
        else if (response.Headers.TryGetValues("Allow", out var headerAllow))
        {
            allow.AddRange(headerAllow);
        }

        var methods = allow
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new RwsOptionsResult(methods, body);
    }

    public async Task<RwsResult> PostAsync(string resource, IReadOnlyDictionary<string, string>? form,
        CancellationToken ct = default)
    {
        ApiVersion = "2.0";
        using var content = new FormUrlEncodedContent(form ?? new Dictionary<string, string>());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(FormContentType);
        using var response = await _http.PostAsync(Url(resource), content, ct).ConfigureAwait(false);
        return await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<RwsResult> PostRawAsync(string resource, string body, CancellationToken ct = default)
    {
        ApiVersion = "2.0";
        using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(FormContentType);
        using var response = await _http.PostAsync(Url(resource), content, ct).ConfigureAwait(false);
        return await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<RwsResult> PutFormAsync(string resource, IReadOnlyDictionary<string, string> form,
        CancellationToken ct = default)
    {
        ApiVersion = "2.0";
        using var content = new FormUrlEncodedContent(form);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(FormContentType);
        using var response = await _http.PutAsync(Url(resource), content, ct).ConfigureAwait(false);
        return await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<RwsFileMetadata> HeadAsync(string resource, CancellationToken ct = default)
    {
        ApiVersion = "2.0";
        using var request = new HttpRequestMessage(HttpMethod.Head, Url(resource));
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new RwsException($"读取文件元数据失败：HTTP {(int)response.StatusCode}",
                (int)response.StatusCode);
        }

        return new RwsFileMetadata(
            response.Content.Headers.ContentLength,
            response.Content.Headers.ContentType?.ToString(),
            response.Content.Headers.LastModified,
            response.Headers.ETag?.Tag);
    }

    public Task<byte[]> GetBytesAsync(string resource, CancellationToken ct = default) =>
        GetBytesAsync(resource, "*/*", ct);

    public async Task<byte[]> GetBytesAsync(string resource, string? accept, CancellationToken ct = default)
    {
        ApiVersion = ExtractVersion(accept) ?? "2.0";
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(resource));
        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw CreateError(response.StatusCode, body);
        }

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task PutBytesAsync(string resource, byte[] data, CancellationToken ct = default)
    {
        ApiVersion = "2.0";
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream;v=2.0");
        using var response = await _http.PutAsync(Url(resource), content, ct).ConfigureAwait(false);
        await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string resource, CancellationToken ct = default)
    {
        ApiVersion = "2.0";
        using var response = await _http.DeleteAsync(Url(resource), ct).ConfigureAwait(false);
        await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    private async Task<RwsResult> ParseResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateError(response.StatusCode, body);
        }

        var result = RwsXhtmlParser.Parse(body);
        result.Location = response.Headers.Location?.ToString();
        return result;
    }

    private static RwsException CreateError(HttpStatusCode statusCode, string body)
    {
        long? code = null;
        string? message = null;
        RwsXhtmlParser.TryGetError(body, out code, out message);

        if ((int)statusCode == 401)
        {
            return new RwsException("RWS 认证失败：用户名或密码错误", 401, code);
        }

        return new RwsException(message ?? $"RWS 请求失败：HTTP {(int)statusCode} {statusCode}",
            (int)statusCode, code);
    }

    private static string? ExtractVersion(string? accept)
    {
        if (string.IsNullOrEmpty(accept))
        {
            return null;
        }

        var marker = accept.IndexOf(";v=", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? null : accept[(marker + 3)..].Split(';')[0].Trim();
    }

    private string Url(string resource) => BaseUri.ToString().TrimEnd('/') + "/" + resource.TrimStart('/');

    public void Dispose() => _http.Dispose();
}
