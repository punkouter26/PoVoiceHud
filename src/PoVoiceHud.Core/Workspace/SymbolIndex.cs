using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace PoVoiceHud.Core.Workspace;

public enum SymbolKind { File, Entity, Adr }

/// <summary>Something a speaker can name: a tracked file, an EF Core entity, or an ADR title. Path is repo-relative.</summary>
public sealed record Symbol(string Name, SymbolKind Kind, string Path, int Line = 1);

public sealed partial class SymbolIndex
{
    private SymbolIndex(List<Symbol> symbols) => Symbols = symbols;

    public IReadOnlyList<Symbol> Symbols { get; }

    public static SymbolIndex Build(string root)
    {
        var symbols = new List<Symbol>();
        using var repo = new Repository(root);
        foreach (var entry in repo.Index)
        {
            var path = entry.Path;
            symbols.Add(new Symbol(System.IO.Path.GetFileNameWithoutExtension(path), SymbolKind.File, path));

            var full = System.IO.Path.Combine(root, path);
            if (!File.Exists(full)) continue;
            if (path.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase))
                symbols.AddRange(Entities(path, File.ReadLines(full)));
            else if (path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var title = File.ReadLines(full).FirstOrDefault(l => l.StartsWith("# "));
                if (title is not null) symbols.Add(new Symbol(title[2..].Trim(), SymbolKind.Adr, path));
            }
        }
        return new SymbolIndex(symbols);
    }

    private static IEnumerable<Symbol> Entities(string path, IEnumerable<string> lines)
    {
        var seen = new HashSet<string>();
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            var match = EntityCall().Match(line);
            if (match.Success && seen.Add(match.Groups[1].Value))
                yield return new Symbol(match.Groups[1].Value.Split('.')[^1], SymbolKind.Entity, path, number);
        }
    }

    [GeneratedRegex(@"\.Entity\(""([\w.]+)""")]
    private static partial Regex EntityCall();
}
