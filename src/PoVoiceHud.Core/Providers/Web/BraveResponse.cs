namespace PoVoiceHud.Core.Providers.Web;

/// <summary>The parts of GET /res/v1/web/search we read.</summary>
public sealed record BraveResponse(BraveWeb? Web);

public sealed record BraveWeb(List<BraveResult>? Results);

public sealed record BraveResult(string Title, string Url, string? Description);
