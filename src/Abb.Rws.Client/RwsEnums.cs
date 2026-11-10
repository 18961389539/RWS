namespace Abb.Rws.Client;

/// <summary>
/// RWS 协议版本。RobotWare 6 (IRC5) 使用 RWS 1.0，RobotWare 7 (OmniCore) 使用 RWS 2.0。
/// </summary>
public enum RwsProtocolVersion
{
    /// <summary>自动探测，依次尝试 RWS 2.0 (HTTPS 443) 和 RWS 1.0 (HTTP 80)。</summary>
    Auto = 0,

    /// <summary>RWS 1.0：IRC5 / RobotWare 6，HTTP Digest，JSON。</summary>
    Rws1 = 1,

    /// <summary>RWS 2.0：OmniCore / RobotWare 7，HTTPS Basic，XHTML。</summary>
    Rws2 = 2
}

/// <summary>
/// 控制权域。RWS 2.0 的 Edit 同时覆盖 RWS 1.0 的 cfg 和 rapid。
/// </summary>
public enum MastershipDomain
{
    /// <summary>RAPID / 配置写权限。RWS 1.0 下会同时申请 cfg 和 rapid。</summary>
    Edit,

    /// <summary>运动控制权限。</summary>
    Motion,

    /// <summary>全部域。</summary>
    All
}
