using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Abb.Rws.Client.Internal;

/// <summary>
/// .NET 没有内置 HTTP Digest 客户端认证，这里实现 RFC 2617 的 MD5 / qop=auth 流程。
/// RWS 1.0 (IRC5) 默认使用 Digest。
/// </summary>
internal sealed class DigestAuthHandler : DelegatingHandler
{
    private static readonly Regex ParameterRegex = new(
        "(?<key>[a-zA-Z0-9_-]+)\\s*=\\s*(?:\"(?<quoted>[^\"]*)\"|(?<bare>[^,]*))",
        RegexOptions.Compiled);

    private readonly string _username;
    private readonly string _password;
    private DigestChallenge? _challenge;
    private int _nonceCount;

    public DigestAuthHandler(string username, string password, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _username = username;
        _password = password;
    }

    /// <summary>用已缓存的 Digest 挑战为 WebSocket 握手生成 Authorization 头。</summary>
    public string? CreateAuthorizationHeader(string method, Uri uri)
    {
        if (_challenge is not { } challenge)
        {
            return null;
        }

        var nonceCount = Interlocked.Increment(ref _nonceCount);
        return challenge.CreateAuthorization(_username, _password, method, uri.PathAndQuery, nonceCount);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_challenge is { } cachedChallenge)
        {
            var response = await SendWithDigestAsync(request, cachedChallenge, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            // nonce 过期等情况，重新握手一次
            response.Dispose();
            _challenge = null;
        }

        var probeRequest = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage probe;
        try
        {
            probe = await base.SendAsync(probeRequest, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            probeRequest.Dispose();
        }

        if (probe.StatusCode != HttpStatusCode.Unauthorized)
        {
            return probe;
        }

        var digestHeader = probe.Headers.WwwAuthenticate
            .FirstOrDefault(h => h.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase));
        if (digestHeader?.Parameter is null)
        {
            return probe;
        }

        probe.Dispose();
        _challenge = DigestChallenge.Parse(digestHeader.Parameter);
        return await SendWithDigestAsync(request, _challenge, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithDigestAsync(HttpRequestMessage original,
        DigestChallenge challenge, CancellationToken cancellationToken)
    {
        var request = await CloneAsync(original, cancellationToken).ConfigureAwait(false);
        try
        {
            var nonceCount = Interlocked.Increment(ref _nonceCount);
            var authorization = challenge.CreateAuthorization(
                _username, _password, request.Method.Method, request.RequestUri!.PathAndQuery, nonceCount);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }

    private sealed class DigestChallenge
    {
        public required string Realm { get; init; }

        public required string Nonce { get; init; }

        public string? Opaque { get; init; }

        public string? Qop { get; init; }

        public string? Algorithm { get; init; }

        public static DigestChallenge Parse(string parameter)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in ParameterRegex.Matches(parameter))
            {
                values[match.Groups["key"].Value] = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["bare"].Value.Trim();
            }

            return new DigestChallenge
            {
                Realm = values.GetValueOrDefault("realm", string.Empty),
                Nonce = values.GetValueOrDefault("nonce", string.Empty),
                Opaque = values.GetValueOrDefault("opaque"),
                Qop = values.GetValueOrDefault("qop"),
                Algorithm = values.GetValueOrDefault("algorithm")
            };
        }

        public string CreateAuthorization(string username, string password, string method, string uri,
            int nonceCount)
        {
            var qop = Qop?.Split(',')
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.Equals("auth", StringComparison.OrdinalIgnoreCase));

            var cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var nc = nonceCount.ToString("x8", CultureInfo.InvariantCulture);

            var ha1 = Md5($"{username}:{Realm}:{password}");
            if (Algorithm?.StartsWith("MD5-sess", StringComparison.OrdinalIgnoreCase) == true)
            {
                ha1 = Md5($"{ha1}:{Nonce}:{cnonce}");
            }

            var ha2 = Md5($"{method}:{uri}");
            var response = qop is null
                ? Md5($"{ha1}:{Nonce}:{ha2}")
                : Md5($"{ha1}:{Nonce}:{nc}:{cnonce}:{qop}:{ha2}");

            var parts = new List<string>
            {
                $"username=\"{username}\"",
                $"realm=\"{Realm}\"",
                $"nonce=\"{Nonce}\"",
                $"uri=\"{uri}\"",
                $"response=\"{response}\""
            };

            if (!string.IsNullOrEmpty(Opaque))
            {
                parts.Add($"opaque=\"{Opaque}\"");
            }

            if (qop is not null)
            {
                parts.Add($"qop={qop}");
                parts.Add($"nc={nc}");
                parts.Add($"cnonce=\"{cnonce}\"");
            }

            if (!string.IsNullOrEmpty(Algorithm))
            {
                parts.Add($"algorithm={Algorithm}");
            }

            return "Digest " + string.Join(", ", parts);
        }

        private static string Md5(string text) =>
            Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
