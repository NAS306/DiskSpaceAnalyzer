using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal sealed record Entry(string Key, string Korean, string Source);
internal static class Program
{
    internal static readonly Dictionary<string, Entry> Entries = new();
    private static readonly Dictionary<string, string> Outputs = new();
    internal static bool Korean(string text) => text.Any(c => c is >= '\uac00' and <= '\ud7a3');
    internal static string Register(string value, string source, string? key = null)
    {
        key ??= "Text." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
        if (Entries.TryGetValue(key, out var existing) && existing.Korean != value) throw new Exception("Resource key collision");
        Entries[key] = new(key, value, source); return key;
    }
    private static void Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\") && !p.EndsWith("_BU.cs") && !p.EndsWith("Verification.cs")))
        {
            string original = File.ReadAllText(path);
            var rewritten = (CompilationUnitSyntax)new Rewriter(path).Visit(CSharpSyntaxTree.ParseText(original).GetRoot())!;
            if (rewritten.ToFullString() != original)
            {
                rewritten = rewritten.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("DiskSpaceAnalyzer.Core.Localization")).WithAlias(SyntaxFactory.NameEquals("I18n")).NormalizeWhitespace().WithTrailingTrivia(SyntaxFactory.EndOfLine("\r\n")));
                Outputs[path] = rewritten.ToFullString();
            }
        }
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories).Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\") && !p.EndsWith("_BU.xaml")))
        {
            string original = File.ReadAllText(path);
            string rewritten = Regex.Replace(original, "(?<a>Text|Content|Header|Title|ToolTip)=\"(?<v>[^\"]*)\"", m =>
            {
                string decoded = WebUtility.HtmlDecode(m.Groups["v"].Value), key;
                var help = Regex.Match(decoded, @"^\{x:Static controls:FeatureHelp\.(\w+)\}$");
                if (help.Success) key = "Help." + help.Groups[1].Value;
                else if (Korean(decoded)) key = Register(decoded, path);
                else return m.Value;
                return m.Groups["a"].Value + "=\"{loc:Tr " + key + "}\"";
            });
            if (original != rewritten)
            {
                rewritten = rewritten.Replace("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"", "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:loc=\"clr-namespace:DiskSpaceAnalyzer.Controls\"");
                Outputs[path] = rewritten;
            }
        }
        Directory.CreateDirectory(Path.Combine(root, "tools/localization"));
        File.WriteAllText(Path.Combine(root, "tools/localization/source.json"), JsonSerializer.Serialize(Entries.Values.OrderBy(x => x.Key), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        if (args.Contains("--apply")) foreach (var pair in Outputs) File.WriteAllText(pair.Key, pair.Value, new UTF8Encoding(false));
        Console.WriteLine($"Resources: {Entries.Count}; source files: {Outputs.Count}; applied: {args.Contains("--apply")}");
    }
    private sealed class Rewriter(string path) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Declaration.Variables.Count == 1 && node.Modifiers.Any(SyntaxKind.ConstKeyword) && node.Declaration.Variables[0].Initializer?.Value is LiteralExpressionSyntax literal && literal.Token.Value is string value && Korean(value))
            {
                string name = node.Declaration.Variables[0].Identifier.ValueText;
                string key = Register(value, path, path.EndsWith("FeatureHelp.cs") ? "Help." + name : null);
                var modifiers = node.Modifiers.Where(t => !t.IsKind(SyntaxKind.ConstKeyword)).ToList();
                if (!modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword))) modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(SyntaxFactory.Space));
                return SyntaxFactory.PropertyDeclaration(node.Declaration.Type, name).WithModifiers(SyntaxFactory.TokenList(modifiers)).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ParseExpression($"I18n.T(\"{key}\")"))).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace().WithTriviaFrom(node);
            }
            return base.VisitFieldDeclaration(node);
        }
        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression) && node.Token.Value is string value && Korean(value)) return SyntaxFactory.ParseExpression($"I18n.T(\"{Register(value, path)}\")").WithTriviaFrom(node);
            return base.VisitLiteralExpression(node);
        }
        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            var visited = (InterpolatedStringExpressionSyntax)base.VisitInterpolatedStringExpression(node)!;
            if (!node.Contents.OfType<InterpolatedStringTextSyntax>().Any(t => Korean(t.TextToken.ValueText))) return visited;
            var format = new StringBuilder(); var expressions = new List<string>();
            foreach (var part in visited.Contents)
            {
                if (part is InterpolatedStringTextSyntax text) format.Append(text.TextToken.ValueText);
                else if (part is InterpolationSyntax interpolation)
                {
                    format.Append('{').Append(expressions.Count);
                    if (interpolation.AlignmentClause is { } align) format.Append(',').Append(align.Value.ToString());
                    if (interpolation.FormatClause is { } clause) format.Append(':').Append(clause.FormatStringToken.ValueText);
                    format.Append('}'); expressions.Add(interpolation.Expression.ToString());
                }
            }
            string key = Register(format.ToString(), path);
            return SyntaxFactory.ParseExpression($"I18n.F(\"{key}\", {string.Join(", ", expressions)})").WithTriviaFrom(node);
        }
    }
}
