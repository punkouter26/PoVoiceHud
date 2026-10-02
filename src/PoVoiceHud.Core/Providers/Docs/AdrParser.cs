using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PoVoiceHud.Core.Providers.Docs;

/// <summary>DecisionLine is the 1-based line of the "## Decision" heading; CodePaths are repo paths the doc names.</summary>
public sealed record AdrInfo(string Title, string? Status, string? Decision, int DecisionLine, IReadOnlyList<string> CodePaths, string Path);

public static partial class AdrParser
{
    public static AdrInfo Parse(string markdown, string path)
    {
        string? title = null, status = null, decision = null, section = null;
        var decisionLine = 1;

        foreach (var block in Markdown.Parse(markdown))
        {
            switch (block)
            {
                case HeadingBlock heading:
                    section = Text(heading.Inline);
                    if (heading.Level == 1) title ??= section;
                    if (section.Equals("Decision", StringComparison.OrdinalIgnoreCase)) decisionLine = heading.Line + 1;
                    break;
                case ParagraphBlock paragraph:
                    var text = Text(paragraph.Inline);
                    if (status is null && StatusLine().Match(text) is { Success: true } m) status = m.Groups[1].Value;
                    if (decision is null && section?.Equals("Decision", StringComparison.OrdinalIgnoreCase) == true) decision = text;
                    break;
            }
        }

        // ponytail: any src/lib/app-rooted path mentioned anywhere counts as "documented code"; refine per repo if noisy.
        var codePaths = CodePath().Matches(markdown).Select(m => m.Value).Distinct().ToList();
        return new AdrInfo(title ?? System.IO.Path.GetFileNameWithoutExtension(path), status, decision, decisionLine, codePaths, path);
    }

    private static string Text(ContainerInline? inline) => inline is null ? "" : string.Concat(inline.Descendants().Select(i => i switch
    {
        LiteralInline literal => literal.Content.ToString(),
        CodeInline code => code.Content,
        LineBreakInline => " ",
        _ => "",
    }));

    [GeneratedRegex(@"Status:\s*(\w+)")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"\b(?:src|lib|app|services|packages)/[\w./-]*\w")]
    private static partial Regex CodePath();
}
