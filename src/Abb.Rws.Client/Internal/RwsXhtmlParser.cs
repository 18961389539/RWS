using System.Xml.Linq;

namespace Abb.Rws.Client.Internal;

/// <summary>
/// 解析 RWS 2.0 的 XHTML 响应。HTTP 事件与普通资源使用同一套结构。
/// </summary>
internal static class RwsXhtmlParser
{
    public static RwsResult Parse(string body)
    {
        var result = new RwsResult { RawBody = body };
        if (string.IsNullOrWhiteSpace(body))
        {
            return result;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(body);
        }
        catch
        {
            return result;
        }

        var state = FindState(document);
        if (state is null)
        {
            return result;
        }

        result.SelfHref = state.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "a" && RelOf(e) == "self")?.Attribute("href")?.Value;
        result.NextHref = state.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "a" && RelOf(e) == "next")?.Attribute("href")?.Value;

        foreach (var li in state.Elements()
                     .Where(e => e.Name.LocalName == "ul")
                     .SelectMany(ul => ul.Elements().Where(e => e.Name.LocalName == "li")))
        {
            result.Items.Add(ParseItem(li));
        }

        return result;
    }

    public static bool TryGetError(string body, out long? code, out string? message)
    {
        code = null;
        message = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            var document = XDocument.Parse(body);
            var status = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "div" && ClassOf(e) == "status");
            if (status is null)
            {
                return false;
            }

            var codeText = status.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "span" && ClassOf(e) == "code")?.Value.Trim();
            message = status.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "span" && ClassOf(e) == "msg")?.Value.Trim();
            if (long.TryParse(codeText, out var parsedCode))
            {
                code = parsedCode;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static XElement? FindState(XDocument document) =>
        document.Descendants().FirstOrDefault(e => e.Name.LocalName == "div" && ClassOf(e) == "state");

    private static RwsItem ParseItem(XElement li)
    {
        var item = new RwsItem
        {
            Title = li.Attribute("title")?.Value,
            Type = li.Attribute("class")?.Value,
            Href = li.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "a" && RelOf(e) == "self")
                ?.Attribute("href")?.Value,
            EventsHref = li.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "a" && RelOf(e) == "events")
                ?.Attribute("href")?.Value
        };

        foreach (var span in li.Descendants().Where(e => e.Name.LocalName == "span"))
        {
            var key = span.Attribute("class")?.Value;
            if (!string.IsNullOrEmpty(key))
            {
                item.Values[key] = span.Value.Trim();
            }
        }

        return item;
    }

    private static string? ClassOf(XElement element) => element.Attribute("class")?.Value;

    private static string? RelOf(XElement element) => element.Attribute("rel")?.Value;
}
