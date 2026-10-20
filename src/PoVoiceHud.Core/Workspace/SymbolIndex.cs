using System.Text.RegularExpressions;
using LibGit2Sharp;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Workspace;

public enum SymbolKind { File, Entity, Adr }

/// <summary>Something a speaker can name: a tracked file, an EF Core entity, or an ADR title. Path is repo-relative.</summary>
public sealed record Symbol(string Name, SymbolKind Kind, string Path, int Line = 1)
{
    /// <summary>Lower-cased camel-split name tokens, computed once so matching does no regex work per utterance.</summary>
    public string[] Tokens { get; } = SymbolMatcher.NameTokens(Name);

    public string AbsolutePath(string root) => System.IO.Path.Combine(root, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
}

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

            var isSnapshot = path.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase);
            var isDoc = path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            if (!isSnapshot && !isDoc) continue;   // only these two kinds need the file read (one stat per entry otherwise)

            var full = System.IO.Path.Combine(root, path);
            if (!File.Exists(full)) continue;
            if (isSnapshot)
                symbols.AddRange(Entities(path, File.ReadLines(full)));
            else
            {
                var title = File.ReadLines(full).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
                if (title is not null) symbols.Add(new Symbol(title[2..].Trim(), SymbolKind.Adr, path));
            }
        }
        return new SymbolIndex(symbols);
    }

    // Words that recur in every codebase's file names: Whisper already knows them, so they would only take room in its prompt.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test", "Tests", "Service", "Services", "Controller", "Model", "Models", "View", "Views", "Helper", "Helpers", "Extensions",
        "Factory", "Provider", "Options", "Config", "Configuration", "Base", "Program", "Startup", "Handler", "Manager", "Repository",
        "Context", "Index", "Interface", "Request", "Response", "Command", "Query", "Settings", "Snapshot", "Migration", "Page",
    };

    /// <summary>
    /// The repository's own words for Whisper's prompt, most useful first, so that names it would otherwise mishear come out
    /// spelled as in the code: EF entity names, then the parts of file names that recur most (the project's domain terms).
    /// </summary>
    public IEnumerable<string> Vocabulary()
    {
        var entities = Symbols.Where(s => s.Kind == SymbolKind.Entity).Select(s => s.Name);
        var recurring = Symbols.Where(s => s.Kind == SymbolKind.File)
            .SelectMany(s => NamePart().Matches(s.Name).Select(m => m.Value))
            .Where(w => w.Length >= 4 && !Generic.Contains(w))
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.First());
        return entities.Concat(recurring).Distinct(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>One part of a PascalCase name: "PMIS" and "Sync" in "PMISSyncJob", "Invoice" in "InvoiceService".</summary>
    [GeneratedRegex("[A-Z]{2,}(?=[A-Z][a-z]|[^A-Za-z]|$)|[A-Z][a-z0-9]+")]
    private static partial Regex NamePart();
}
