using Whisper.net.Ggml;

namespace PoVoiceHud.Core.Asr;

public readonly record struct ModelPaths(string Whisper, string Silero);

/// <summary>Downloads the Whisper GGML and Silero VAD models once into %LOCALAPPDATA%\PoVoiceHud\models.</summary>
public static class ModelStore
{
    private const string SileroUrl = "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx";

    public static string Directory { get; } = Path.Combine(Config.AppPaths.DataDir, "models");

    public static readonly string[] WhisperModels = ["tiny.en", "base.en", "small.en"];

    public static async Task<ModelPaths> EnsureAsync(string whisperModel, IProgress<double>? progress, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var whisper = Path.Combine(Directory, $"ggml-{whisperModel}.bin");
        var silero = Path.Combine(Directory, "silero_vad.onnx");

        await Task.WhenAll(
            Present(silero) ? Task.CompletedTask : DownloadSileroAsync(silero, progress, ct),
            Present(whisper) ? Task.CompletedTask : DownloadWhisperAsync(whisperModel, whisper, progress, ct));
        progress?.Report(1);
        return new ModelPaths(whisper, silero);
    }

    private static async Task DownloadSileroAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = new HttpClient();
        await using var src = await http.GetStreamAsync(SileroUrl, ct);
        await SaveAsync(src, path, progress, ct);
    }

    private static async Task DownloadWhisperAsync(string model, string path, IProgress<double>? progress, CancellationToken ct)
    {
        await using var src = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ToGgmlType(model), cancellationToken: ct);
        await SaveAsync(src, path, progress, ct);
    }

    private static bool Present(string path) => File.Exists(path) && new FileInfo(path).Length > 1_000_000;

    private static async Task SaveAsync(Stream src, string path, IProgress<double>? progress, CancellationToken ct)
    {
        var tmp = path + ".part";
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
                // Length is unknown for chunked downloads; report bytes as a fraction of base.en's size as a rough gauge.
                progress?.Report(Math.Min(0.99, total / 150_000_000.0));
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static GgmlType ToGgmlType(string model) => model switch
    {
        "tiny.en" => GgmlType.TinyEn,
        "base.en" => GgmlType.BaseEn,
        "small.en" => GgmlType.SmallEn,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Supported: tiny.en, base.en, small.en"),
    };
}
