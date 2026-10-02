# PoVoiceHud — Capability Map

One solution, three projects. Arrows are the only allowed dependency directions.

```
PoVoiceHud.App (WinUI 3)  ──►  PoVoiceHud.Core (net10.0-windows class lib)
PoVoiceHud.Core.Tests     ──►  PoVoiceHud.Core
```

`PoVoiceHud.Core` never references WinUI. Everything the HUD shows is produced by Core and observed by App.

| Capability | Namespace (Core) | Owns | Depends on | Public surface |
|---|---|---|---|---|
| **Audio** | `PoVoiceHud.Core.Audio` | WASAPI loopback + mic capture (selectable `Mic`/`Remote`/`Both`), mixing, 16 kHz mono PCM16 framing, mute/flush, device-change recovery | NAudio | `IAudioSource` → `event Frame(ReadOnlyMemory<short>, SourceHint)`; `IsMuted` |
| **Segmentation** | `PoVoiceHud.Core.Vad` | Silero VAD over 512-sample frames, 650 ms silence → utterance boundary, 60 s ring buffer | Microsoft.ML.OnnxRuntime | `IUtteranceSegmenter` → `event Utterance(short[] pcm, SourceHint, TimeSpan start)` |
| **Transcription** | `PoVoiceHud.Core.Asr` | Whisper.net CPU, base.en, model download/cache | Whisper.net | `ITranscriber.TranscribeAsync(short[]) → string` |
| **Router** | `PoVoiceHud.Core.Routing` | Noise filter, intent classification, symbol extraction, fan-out to providers | — | `IRouter.RouteAsync(TranscriptLine) → IReadOnlyList<Insight>` |
| **Workspace index** | `PoVoiceHud.Core.Workspace` | Repo root selection, symbol table (files, EF entities, ADR titles), FS watcher, cache | LibGit2Sharp | `IWorkspace` (`Root`, `Symbols`, `Changed` event) |
| **Providers** | `PoVoiceHud.Core.Providers.*` | `Git`, `Schema` (Roslyn EF snapshot), `Docs` (Markdig + freshness), `Pipeline` (AzDO), `Web` (Brave), `FactCheck` (Brave evidence + Azure AI Foundry LLM verdict) — each with a fake; each gated by a Settings toggle | LibGit2Sharp, Roslyn, Markdig, TFS client, HttpClient | `IInsightProvider { Intent Handles; Task<Insight?> QueryAsync(Query) }` |
| **Settings & secrets** | `PoVoiceHud.Core.Config` | `settings.json` in `%LOCALAPPDATA%\PoVoiceHud`, Credential Manager read/write | — | `ISettings`, `ISecretStore` |
| **Pipeline host** | `PoVoiceHud.Core` | Wires Audio → Vad → Asr → Router → Insights; exposes observable streams | all above | `HudPipeline` (`Transcript`, `Insights`, `Status` observables) |
| **HUD** | `PoVoiceHud.App` | Always-on-top acrylic window, Zones 1–3, status bar, settings flyout, global hotkey, `vscode://` launch, docking | WinAppSDK, Core | XAML + ViewModels |
