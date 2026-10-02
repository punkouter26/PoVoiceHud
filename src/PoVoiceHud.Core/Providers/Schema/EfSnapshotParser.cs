using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PoVoiceHud.Core.Providers.Schema;

/// <summary>Reads an EF Core *ModelSnapshot.cs: each modelBuilder.Entity("…", b => { … }) block's fluent chains.</summary>
public static class EfSnapshotParser
{
    private static readonly HashSet<string> ValueTypes =
        ["Guid", "int", "long", "short", "byte", "bool", "decimal", "double", "float", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan"];

    private sealed record Call(string Name, string? TypeArg, string[] Args, int Line);

    public static SchemaModel Parse(string source, string path)
    {
        var entities = new Dictionary<string, EntityInfo>();
        foreach (var call in CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Entity" }) continue;
            if (call.ArgumentList.Arguments is not
                [{ Expression: LiteralExpressionSyntax { Token.Value: string fullName } }, { Expression: LambdaExpressionSyntax lambda }])
                continue;

            if (!entities.TryGetValue(fullName, out var entity))
                entities[fullName] = entity = new EntityInfo(fullName, fullName.Split('.')[^1], Line(call));
            foreach (var statement in lambda.Body.DescendantNodesAndSelf().OfType<ExpressionStatementSyntax>())
                Apply(entity, Chain(statement.Expression));
        }
        return new SchemaModel(path, entities.Values.ToList());
    }

    /// <summary>b.Property&lt;Guid&gt;("TenantId").HasColumnType("x") → [Property, HasColumnType] (root first).</summary>
    private static List<Call> Chain(ExpressionSyntax expression)
    {
        var calls = new List<Call>();
        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation)
        {
            var typeArg = member.Name is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments[0].ToString() : null;
            var args = invocation.ArgumentList.Arguments
                .Select(a => a.Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : a.Expression.ToString())
                .ToArray();
            calls.Add(new Call(member.Name.Identifier.Text, typeArg, args, Line(invocation)));
            expression = member.Expression;
        }
        calls.Reverse();
        return calls;
    }

    private static void Apply(EntityInfo entity, List<Call> chain)
    {
        if (chain.Count == 0) return;
        var root = chain[0];
        bool Has(string name) => chain.Any(c => c.Name == name && c.Args is not ["false"]);
        string? Arg(string name) => chain.FirstOrDefault(c => c.Name == name)?.Args.FirstOrDefault();

        switch (root.Name)
        {
            case "Property" when root.Args.Length > 0:
                var clr = root.TypeArg ?? "?";
                entity.Columns.Add(new ColumnInfo(root.Args[0], clr, Arg("HasColumnType"), Has("IsRequired") || ValueTypes.Contains(clr), root.Line));
                break;
            case "HasKey":
                entity.Keys.AddRange(root.Args);
                break;
            case "HasIndex":
                entity.Indexes.Add(string.Join(", ", root.Args));
                break;
            case "ToTable" when root.Args.Length > 0:
                entity.Table = root.Args[0];
                break;
            case "HasOne" when root.Args.Length > 0:
                entity.ForeignKeys.Add(new ForeignKeyInfo(
                    Arg("HasForeignKey") ?? "", root.Args[0].Split('.')[^1], root.Args.ElementAtOrDefault(1) ?? "", Has("IsRequired"), root.Line));
                break;
        }
    }

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
