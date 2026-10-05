using System.Security.Cryptography;
using Whisper.net.Ggml;

namespace PoVoiceHud.Core.Asr;

public readonly record struct ModelPaths(string Whisper, string Silero);

/// <summary>
/// Downloads Whisper base.en (GGML) and the Silero VAD model once into %LOCALAPPDATA%\PoVoiceHud\models.
/// Both downloads are checked against a pinned SHA-256 before they are put in place.
/// </summary>
public static class ModelStore
{
    // Pinned to a commit, not a branch, so the file cannot change underneath the hash.
    private const string SileroUrl = "https://github.com/snakers4/silero-vad/raw/1e261b036686cd0017d500ee96acd1c4ba572a9d/src/silero_vad/data/silero_vad.onnx";
    private const string SileroSha256 = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";
    private const string WhisperSha256 = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";   // ggml-base.en.bin

    public static string Directory { get; } = Path.Combine(Config.AppPaths.DataDir, "models");

    public static async Task<ModelPaths> EnsureAsync(IProgress<double>? progress, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var whisper = Path.Combine(Directory, "ggml-base.en.bin");
        var silero = Path.Combine(Directory, "silero_vad.onnx");

        // Only the 148 MB Whisper download reports progress; the 2 MB VAD model would just make the gauge jump.
        await Task.WhenAll(
            Present(silero) ? Task.CompletedTask : DownloadSileroAsync(silero, ct),
            Present(whisper) ? Task.CompletedTask : DownloadWhisperAsync(whisper, progress, ct));
        progress?.Report(1);
        return new ModelPaths(whisper, silero);
    }

    private static async Task DownloadSileroAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        await using var src = await http.GetStreamAsync(SileroUrl, ct);
        await SaveAsync(src, path, SileroSha256, null, ct);
    }

    private static async Task DownloadWhisperAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        await using var src = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.BaseEn, cancellationToken: ct);
        await SaveAsync(src, path, WhisperSha256, progress, ct);
    }

    private static bool Present(string path) => File.Exists(path) && new FileInfo(path).Length > 1_000_000;

    private static async Task SaveAsync(Stream src, string path, string sha256, IProgress<double>? progress, CancellationToken ct)
    {
        var tmp = path + ".part";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
                // Length is unknown for chunked downloads; report bytes as a fraction of base.en's size as a rough gauge.
                progress?.Report(Math.Min(0.99, total / 150_000_000.0));
            }
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (actual != sha256)
        {
            File.Delete(tmp);
            throw new InvalidDataException($"{Path.GetFileName(path)} failed its integrity check (SHA-256 {actual[..12]}…): the download was discarded.");
        }
        File.Move(tmp, path, overwrite: true);
    }
}
