namespace PoVoiceHud.Core.Routing;

[Flags]
public enum Intent
{
    None = 0,
    Git = 1,
    Schema = 2,
    Docs = 4,
    Pipeline = 8,
    FactCheck = 32,   // 16 belonged to the removed web source and stays unused
    Code = 64,        // research on sentences that name files or entities in the repository
}
