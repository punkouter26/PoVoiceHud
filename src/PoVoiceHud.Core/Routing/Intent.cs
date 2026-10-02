namespace PoVoiceHud.Core.Routing;

[Flags]
public enum Intent
{
    None = 0,
    Git = 1,
    Schema = 2,
    Docs = 4,
    Pipeline = 8,
    Web = 16,
}
