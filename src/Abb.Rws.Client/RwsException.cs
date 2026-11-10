namespace Abb.Rws.Client;

/// <summary>
/// RWS 调用异常，包含 HTTP 状态码和控制器返回的错误码。
/// </summary>
public sealed class RwsException : Exception
{
    public RwsException(string message, int httpStatusCode = 0, long? robotErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        RobotErrorCode = robotErrorCode;
    }

    /// <summary>HTTP 状态码，0 表示不是 HTTP 层错误。</summary>
    public int HttpStatusCode { get; }

    /// <summary>ABB 控制器错误码（响应中的 status/code）。</summary>
    public long? RobotErrorCode { get; }
}
