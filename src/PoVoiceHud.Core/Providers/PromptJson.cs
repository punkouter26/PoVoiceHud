using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoVoiceHud.Core.Providers;

/// <summary>
/// The user message as JSON, which keeps a transcript unmistakably data. Relaxed escaping sends code and quotes as written
/// instead of the \u003C-style escapes the model has to read past.
/// </summary>
public static class PromptJson
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}
