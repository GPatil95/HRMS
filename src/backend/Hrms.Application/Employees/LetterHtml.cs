using System.Text;
using System.Text.RegularExpressions;

namespace Hrms.Application.Employees;

/// <summary>
/// Allowlist HTML validation + document composition for letter templates and issued letters.
/// No external resource loading is ever permitted: no src/href attributes, no url() in styles,
/// no event-handler attributes, no scripts/iframes/objects. This keeps PDF generation free of
/// SSRF and remote-fetch surfaces regardless of template author.
/// </summary>
public static partial class LetterHtml
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "span", "br", "hr",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "b", "i", "u", "s", "strong", "em", "small", "mark", "sub", "sup",
        "ul", "ol", "li", "blockquote", "pre", "code",
        "table", "thead", "tbody", "tfoot", "tr", "td", "th", "colgroup", "col", "caption",
        "section", "article", "header", "footer", "abbr",
    };

    /// <summary>Safe per-tag attributes. Only presentational attributes needed for letters.</summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["*"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "class", "style", "title", "dir", "align" },
        ["td"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan", "width", "align", "valign" },
        ["th"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan", "width", "align", "valign", "scope" },
        ["table"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "width", "cellpadding", "cellspacing", "border" },
        ["col"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "width", "span" },
    };

    /// <summary>Validates template HTML against the allowlist; returns problems (empty = valid).</summary>
    public static IReadOnlyList<string> Validate(string? html)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return problems;
        }

        foreach (Match m in TagPattern().Matches(html))
        {
            var tag = m.Groups[2].Value;
            if (!AllowedTags.Contains(tag))
            {
                problems.Add($"tag <{tag}> is not allowed");
            }
        }

        foreach (Match m in AttributePattern().Matches(html))
        {
            var tag = m.Groups[1].Value;
            var attr = m.Groups[2].Value;
            if (attr.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"event attribute '{attr}' is not allowed");
                continue;
            }

            var allowed = AllowedAttributes.TryGetValue(tag, out var set) ? set : AllowedAttributes["*"];
            var wildcardOk = AllowedAttributes["*"].Contains(attr);
            if (!allowed.Contains(attr) && !wildcardOk)
            {
                problems.Add($"attribute '{attr}' is not allowed on <{tag}>");
            }

            if (attr.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                var value = m.Groups[4].Success ? m.Groups[4].Value : m.Groups[3].Value;
                if (UrlInStyle().IsMatch(value))
                {
                    problems.Add("style must not fetch external resources (url()/import/expression)");
                }
            }
        }

        return problems;
    }

    /// <summary>Wraps rendered letter body in a full standalone document with the letterhead header/footer.</summary>
    public static string ComposeDocument(string title, string? headerHtml, string bodyHtml, string? footerHtml, int marginMm = 15)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>")
          .Append(System.Net.WebUtility.HtmlEncode(title))
          .Append("</title><style>")
          .Append("@page { margin: ").Append(marginMm).Append("mm; }")
          .Append("body { font-family: 'DejaVu Sans', Helvetica, Arial, sans-serif; font-size: 11pt; color: #111; }")
          .Append("header.lh { margin-bottom: 12mm; } footer.lf { margin-top: 12mm; }")
          .Append("</style></head><body>");
        if (!string.IsNullOrWhiteSpace(headerHtml))
        {
            sb.Append("<header class=\"lh\">").Append(headerHtml).Append("</header>");
        }
        sb.Append(bodyHtml);
        if (!string.IsNullOrWhiteSpace(footerHtml))
        {
            sb.Append("<footer class=\"lf\">").Append(footerHtml).Append("</footer>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    [GeneratedRegex(@"<\s*(/)?\s*([a-zA-Z][a-zA-Z0-9]*)", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    [GeneratedRegex("<\\s*([a-zA-Z][a-zA-Z0-9]*)[^>]*?([a-zA-Z_][a-zA-Z0-9_\\-]*)\\s*=\\s*(\"([^\"]*)\"|'[^']*')", RegexOptions.Compiled)]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"url\s*\(|@import|expression\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlInStyle();
}
