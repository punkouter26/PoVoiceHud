namespace PoVoiceHud.Core.Routing;

[Flags]
public enum Intent
{
    None = 0,
    Git = 1,
    Schema = 2,
    Docs = 4,
    Pipeline = 8,
    FactCheck = 32,   // 16 was the removed web-answers source; value kept stable for settings.json
}
