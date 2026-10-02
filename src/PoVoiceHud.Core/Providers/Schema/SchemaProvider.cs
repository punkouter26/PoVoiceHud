using System.Text.RegularExpressions;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Providers.Schema;

/// <summary>Answers table/column questions from the EF Core model snapshot the symbol index pointed at.</summary>
public sealed partial class SchemaProvider(Func<string?> repoRoot) : IInsightProvider
{
    private const double MinScore = 0.8;
    private readonly Dictionary<string, (DateTime Stamp, SchemaModel Model)> _cache = [];

    public Intent Handles => Intent.Schema;

    public Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var root = repoRoot();
        var candidates = query.Symbols.Where(m => m.Score >= MinScore && m.Symbol.Kind == SymbolKind.Entity).ToList();
        if (root is null || candidates.Count == 0) return Task.FromResult<Insight?>(null);

        var spoken = Tokens(query.Text);
        // Subject = the entity whose column covers the most spoken tokens ("tenant id" → Inspection.TenantId beats Tenant.Id),
        // falling back to the entity named earliest in the sentence.
        var best = candidates
            .Select(m => (Match: m, Entity: Load(root, m.Symbol.Path).Entities.FirstOrDefault(e => e.Name == m.Symbol.Name)))
            .Where(x => x.Entity is not null)
            .Select(x => (x.Match, Entity: x.Entity!, Column: x.Entity!.Columns
                .Select(c => (Column: c, Covered: Coverage(spoken, c.Name)))
                .Where(c => c.Covered > 0)
                .OrderByDescending(c => c.Covered)
                .Select(c => c.Column)
                .FirstOrDefault()))
            .OrderByDescending(x => x.Column is null ? 0 : Coverage(spoken, x.Column.Name))
            .ThenBy(x => Position(query.Text, x.Entity.Name))
            .FirstOrDefault();
        if (best.Entity is null) return Task.FromResult<Insight?>(null);

        var (match, entity, column) = best;
        var model = Load(root, match.Symbol.Path);
        var (headline, answer, line) = column is null
            ? ($"[SCHEMA] {entity.Name}", Summary(entity, model), entity.Line)
            : ($"[SCHEMA] {entity.Name}.{column.Name}", Describe(query.Text, entity, column, model), column.Line);

        var jumps = new List<Jump>();
        var source = query.Symbols.FirstOrDefault(m => m.Symbol.Kind == SymbolKind.File && m.Symbol.Name == entity.Name)?.Symbol;
        if (source is not null)
        {
            var sourcePath = Absolute(root, source.Path);
            jumps.Add(new Jump(sourcePath, column is null ? 1 : LineOf(sourcePath, column.Name)));
        }
        jumps.Add(new Jump(Absolute(root, model.Path), line));

        return Task.FromResult<Insight?>(new Insight(Intent.Schema, headline, answer, jumps, match.Score));
    }

    private static string Describe(string text, EntityInfo entity, ColumnInfo column, SchemaModel model)
    {
        var fk = entity.ForeignKeys.FirstOrDefault(f => f.Column == column.Name);
        var indexed = entity.Indexes.Any(i => i.Split(", ").Contains(column.Name));

        var description = $"{entity.Name}.{column.Name} is {(column.Required ? "a required" : "an optional")} {column.ColumnType ?? column.ClrType}";
        // ponytail: assumes the principal key is "Id"; read HasPrincipalKey when a repo needs it.
        if (fk is not null) description += $" foreign key to {TableOf(model, fk.PrincipalEntity)}.Id";
        if (indexed) description += ", indexed";
        description += ".";

        var lower = text.ToLowerInvariant();
        var verdict =
            lower.Contains("nullable") || lower.Contains("optional") ? (column.Required ? "No" : "Yes")
            : lower.Contains("foreign key") ? (fk is not null ? "Yes" : "No")
            : lower.Contains("required") || lower.Contains("mandatory") ? (column.Required ? "Yes" : "No")
            : lower.Contains("index") ? (indexed ? "Yes" : "No")
            : null;
        return verdict is null ? description : $"{verdict}. {description}";
    }

    private static string Summary(EntityInfo entity, SchemaModel model)
    {
        var parts = new List<string>
        {
            $"Table {entity.Table ?? entity.Name}: {entity.Columns.Count} columns ({string.Join(", ", entity.Columns.Take(6).Select(c => c.Name))})",
        };
        if (entity.Keys.Count > 0) parts.Add($"key {string.Join(", ", entity.Keys)}");
        parts.AddRange(entity.ForeignKeys.Select(f => $"FK {f.Column} → {TableOf(model, f.PrincipalEntity)}"));
        parts.AddRange(entity.Indexes.Select(i => $"index on {i}"));
        return string.Join(", ", parts) + ".";
    }

    private static string TableOf(SchemaModel model, string entityName) =>
        model.Entities.FirstOrDefault(e => e.Name == entityName)?.Table ?? entityName;

    private SchemaModel Load(string root, string relativePath)
    {
        var path = Absolute(root, relativePath);
        var stamp = File.GetLastWriteTimeUtc(path);
        lock (_cache)
        {
            if (_cache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Model;
            var model = EfSnapshotParser.Parse(File.ReadAllText(path), relativePath);
            _cache[path] = (stamp, model);
            return model;
        }
    }

    private static string Absolute(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static int LineOf(string path, string identifier)
    {
        var pattern = new Regex($@"\b{Regex.Escape(identifier)}\b");
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (pattern.IsMatch(line)) return number;
        }
        return 1;
    }

    private static HashSet<string> Tokens(string text) =>
        Token().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToHashSet();

    /// <summary>Number of the column's camel-case tokens heard, or 0 unless all of them were.</summary>
    private static int Coverage(HashSet<string> spoken, string columnName)
    {
        var tokens = Token().Matches(columnName).Select(m => m.Value.ToLowerInvariant()).ToArray();
        return tokens.All(spoken.Contains) ? tokens.Length : 0;
    }

    private static int Position(string text, string name)
    {
        var at = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? int.MaxValue : at;
    }

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex Token();
}
