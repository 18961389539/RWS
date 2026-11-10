namespace Abb.Rws.Client;

/// <summary>
/// 连接选项。
/// </summary>
public sealed class RwsClientOptions
{
    /// <summary>控制器地址，可以是 IP、主机名，或完整 URL（如 https://192.168.4.10）。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>端口。为空时 RWS 1.0 用 80，RWS 2.0 用 443。</summary>
    public int? Port { get; set; }

    /// <summary>是否使用 HTTPS。为空时由协议版本决定。</summary>
    public bool? UseHttps { get; set; }

    /// <summary>RWS 用户名。</summary>
    public string Username { get; set; } = "Default User";

    /// <summary>RWS 密码。</summary>
    public string Password { get; set; } = "robotics";

    /// <summary>协议版本。默认自动探测。</summary>
    public RwsProtocolVersion Version { get; set; } = RwsProtocolVersion.Auto;

    /// <summary>是否校验 TLS 证书。ABB 控制器默认使用自签名证书，因此默认关闭。</summary>
    public bool VerifyCertificate { get; set; }

    /// <summary>单次 HTTP 请求超时。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
