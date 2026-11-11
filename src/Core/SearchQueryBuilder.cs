using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Core;

public sealed record SearchQueryOptions(string Extensions = "zip", string Comparison = ">", string Size = "500", string Unit = "MB", string? ScopePath = null);

public static class SearchQueryBuilder
{
    public static string Build(SearchQueryOptions options)
    {
        var terms = new List<string> { "file:" };
        if (!string.IsNullOrWhiteSpace(options.ScopePath))
        {
            string path = options.ScopePath.Trim().Replace('/', '\\');
            if (!Path.IsPathFullyQualified(path) || path.Contains('"') || path.Contains('*') || path.Contains('?'))
                throw new ArgumentException(I18n.T("Text.28EF637F8BC1"));
            path = Path.GetFullPath(path).TrimEnd('\\');
            terms.Add($"\"{path}\\\"");
        }
        string extensions = options.Extensions.Trim();
        if (extensions.Length > 0 && extensions != "*")
        {
            var values = extensions.Split([';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.StartsWith("*.") ? e[2..] : e.TrimStart('.'))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length == 0 || values.Any(e => !Regex.IsMatch(e, @"^[a-zA-Z0-9][a-zA-Z0-9._+\-]*$")))
                throw new ArgumentException(I18n.T("Text.7DFADE74A273"));
            terms.Add("<" + string.Join("|", values.Select(e => "*." + e.ToLowerInvariant())) + ">");
        }
        if (options.Comparison.Length > 0)
        {
            if (!new[] { ">", ">=", "<", "<=", "=" }.Contains(options.Comparison)) throw new ArgumentException(I18n.T("Text.5BCD847D4B60"));
            if (!decimal.TryParse(options.Size.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal size) || size < 0)
                throw new ArgumentException(I18n.T("Text.819D9F5D825A"));
            int power = Array.IndexOf(new[] { "B", "KB", "MB", "GB", "TB" }, options.Unit.ToUpperInvariant());
            if (power < 0) throw new ArgumentException(I18n.T("Text.883B969D585B"));
            decimal maximum = long.MaxValue;
            for (int i = 0; i < power; i++) maximum /= 1024;
            if (size > maximum) throw new ArgumentException(I18n.T("Text.7C78E0EB8F11"));
            terms.Add($"size:{(options.Comparison == "=" ? "" : options.Comparison)}{size.ToString("0.############################", CultureInfo.InvariantCulture)}{options.Unit.ToLowerInvariant()}");
        }
        return string.Join(" ", terms);
    }
}
