namespace PoVoiceHud.Core.Providers.Schema;

public sealed record ColumnInfo(string Name, string ClrType, string? ColumnType, bool Required, int Line);

public sealed record ForeignKeyInfo(string Column, string PrincipalEntity, string Navigation, bool Required, int Line);

public sealed record EntityInfo(string FullName, string Name, int Line)
{
    public string? Table { get; set; }
    public List<ColumnInfo> Columns { get; } = [];
    public List<string> Keys { get; } = [];
    public List<string> Indexes { get; } = [];
    public List<ForeignKeyInfo> ForeignKeys { get; } = [];
}

public sealed record SchemaModel(string Path, IReadOnlyList<EntityInfo> Entities);
