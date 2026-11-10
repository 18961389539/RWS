using System.Net;
using System.Text.Json;

namespace Abb.Rws.Client.Internal;

/// <summary>
/// RWS 1.0：HTTP + Digest 认证，响应为 JSON（自动追加 ?json=1）。
/// </summary>
internal sealed class Rws1Protocol : IRwsProtocol
{
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    private readonly DigestAuthHandler _digestHandler;

    public Rws1Protocol(Uri baseUri, string username, string password, bool verifyCertificate, TimeSpan timeout)
    {
        BaseUri = baseUri;
        var inner = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true
        };
        if (!verifyCertificate)
        {
            inner.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;
        }

        _digestHandler = new DigestAuthHandler(username, password, inner);
        _http = new HttpClient(_digestHandler)
        {
            BaseAddress = baseUri,
            Timeout = timeout
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    public RwsProtocolVersion Version => RwsProtocolVersion.Rws1;

    public Uri BaseUri { get; }

    public string? ApiVersion => null;

    public CookieContainer Cookies => _cookies;

    public string? CreateWebSocketAuthorization(Uri uri) =>
        _digestHandler.CreateAuthorizationHeader("GET", uri);

    public Task<RwsResult> GetAsync(string resource, CancellationToken ct = default) =>
        GetAsync(resource, null, ct);

    public async Task<RwsResult> GetAsync(string resource, string? accept, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(WithJson(resource)));
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
        using var content = new FormUrlEncodedContent(form ?? new Dictionary<string, string>());
        using var response = await _http.PostAsync(Url(WithJson(resource)), content, ct).ConfigureAwait(false);
        return await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<RwsResult> PostRawAsync(string resource, string body, CancellationToken ct = default)
    {
        using var content = new StringContent(body, System.Text.Encoding.UTF8,
            "application/x-www-form-urlencoded");
        using var response = await _http.PostAsync(Url(WithJson(resource)), content, ct).ConfigureAwait(false);
        return await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<RwsResult> PutFormAsync(string resource, IReadOnlyDictionary<string, string> form,
        CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PutAsync(Url(resource), content, ct).ConfigureAwait(false);
        return await ParseResponseAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<RwsFileMetadata> HeadAsync(string resource, CancellationToken ct = default)
    {
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
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(resource));
        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new RwsException($"下载失败：HTTP {(int)response.StatusCode}", (int)response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task PutBytesAsync(string resource, byte[] data, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await _http.PutAsync(Url(resource), content, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string resource, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync(Url(resource), ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw CreateError(response.StatusCode, body);
        }
    }

    private async Task<RwsResult> ParseResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateError(response.StatusCode, body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new RwsResult();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new RwsException("无法解析控制器返回的 JSON", (int)response.StatusCode, null, ex);
        }

        using (document)
        {
            var result = ParseJson(document.RootElement);
            result.RawBody = body;
            result.Location = response.Headers.Location?.ToString();
            return result;
        }
    }

    private static RwsResult ParseJson(JsonElement root)
    {
        var result = new RwsResult();

        if (root.TryGetProperty("_links", out var links))
        {
            if (links.TryGetProperty("self", out var self) && self.TryGetProperty("href", out var selfHref))
            {
                result.SelfHref = selfHref.GetString();
            }

            if (links.TryGetProperty("next", out var next) && next.TryGetProperty("href", out var nextHref))
            {
                result.NextHref = nextHref.GetString();
            }
        }

        JsonElement state;
        if (root.TryGetProperty("_embedded", out var embedded) && embedded.TryGetProperty("_state", out state))
        {
            // 常见形态
        }
        else if (root.TryGetProperty("_state", out state))
        {
            // 少数资源直接在根节点返回
        }
        else
        {
            return result;
        }

        if (state.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in state.EnumerateArray())
            {
                AddItem(result, element);
            }
        }
        else if (state.ValueKind == JsonValueKind.Object)
        {
            AddItem(result, state);
        }

        return result;
    }

    private static void AddItem(RwsResult result, JsonElement element)
    {
        var item = new RwsItem();
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "_title":
                    item.Title = property.Value.ToString();
                    break;
                case "_type":
                    item.Type = property.Value.ToString();
                    break;
                case "_href":
                    item.Href = property.Value.ToString();
                    break;
                case "_links":
                    if (property.Value.ValueKind == JsonValueKind.Object &&
                        property.Value.TryGetProperty("self", out var self) &&
                        self.TryGetProperty("href", out var href))
                    {
                        item.Href = href.GetString();
                    }

                    break;
                default:
                    if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                        or JsonValueKind.True or JsonValueKind.False)
                    {
                        item.Values[property.Name] = property.Value.ToString();
                    }

                    break;
            }
        }

        result.Items.Add(item);
    }

    private static RwsException CreateError(HttpStatusCode statusCode, string body)
    {
        long? code = null;
        string? message = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                JsonElement status;
                if (root.TryGetProperty("_embedded", out var embedded) &&
                    embedded.TryGetProperty("status", out status))
                {
                    // 标准错误位置
                }
                else if (root.TryGetProperty("status", out status))
                {
                    // 少数接口
                }
                else
                {
                    status = default;
                }

                if (status.ValueKind == JsonValueKind.Object)
                {
                    if (status.TryGetProperty("code", out var codeElement))
                    {
                        if (codeElement.ValueKind == JsonValueKind.Number && codeElement.TryGetInt64(out var number))
                        {
                            code = number;
                        }
                        else if (long.TryParse(codeElement.ToString(), out var parsed))
                        {
                            code = parsed;
                        }
                    }

                    if (status.TryGetProperty("msg", out var messageElement))
                    {
                        message = messageElement.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // 保留 HTTP 层错误信息
            }
        }

        if ((int)statusCode == 401)
        {
            return new RwsException("RWS 认证失败：用户名或密码错误", 401, code);
        }

        return new RwsException(message ?? $"RWS 请求失败：HTTP {(int)statusCode} {statusCode}",
            (int)statusCode, code);
    }

    private static string WithJson(string resource) =>
        resource.Contains('?') ? resource + "&json=1" : resource + "?json=1";

    private string Url(string resource) => BaseUri.ToString().TrimEnd('/') + "/" + resource.TrimStart('/');

    public void Dispose() => _http.Dispose();
}
