namespace PoVoiceHud.Core.Providers.Pipeline;

public sealed record BuildRun(
    int Id,
    string Number,
    string Definition,
    string Status,
    string? Result,
    string Branch,
    string? SourceVersion,
    DateTimeOffset? Start,
    DateTimeOffset? Finish,
    Uri WebUrl,
    string? FailedStage);

public interface IBuildClient
{
    Task<IReadOnlyList<BuildRun>> GetRecentBuildsAsync(string branch, int top, CancellationToken ct);
}
