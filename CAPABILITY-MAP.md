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
| **Transcription** | `PoVoiceHud.Core.Asr` | Whisper.net CPU, base.en, model download/cache, glossary prompt (settings vocabulary + repository names) | Whisper.net | `ITranscriber.TranscribeAsync(short[]) → string`; `WhisperTranscriber.Prompt` |
| **Router** | `PoVoiceHud.Core.Routing` | Noise filter, intent classification, symbol extraction, fan-out to providers | — | `IRouter.RouteAsync(TranscriptLine) → IReadOnlyList<Insight>` |
| **Workspace index** | `PoVoiceHud.Core.Workspace` | Repo root selection, symbol table (files, EF entities, ADR titles), FS watcher, cache | LibGit2Sharp | `IWorkspace` (`Root`, `Symbols`, `Changed` event) |
| **Providers** | `PoVoiceHud.Core.Providers.*` | `Git`, `Schema` (Roslyn EF snapshot), `Docs` (Markdig + freshness), `Pipeline` (AzDO), `FactCheck` (OpenAI-compatible LLM verdict: Azure AI Foundry or Ollama, with Wikipedia passages as evidence) — each gated by a Settings toggle | LibGit2Sharp, Roslyn, Markdig, HttpClient | `IInsightProvider { Intent Handles; Task<Insight?> QueryAsync(Query) }`; `IEvidenceSource` |
| **Sessions** | `PoVoiceHud.Core.Sessions` | `sessions\{date}.jsonl` (heard, cards, 👍/👎, recaps), the current conversation, weekly ratings, Markdown recaps | FactCheck's `ILanguageModel` | `SessionLog`, `RecapWriter.WriteAsync → Recap` |
| **Diagnostics** | `PoVoiceHud.Core.Diagnostics` | `logs\{date}.log` (14 days), the diagnostics zip (no transcripts, no secrets) | Microsoft.Extensions.Logging | `FileLoggerProvider`, `DiagnosticsBundle.Create` |
| **Effects physics** | `PoVoiceHud.Core.Fx` | Spring, particles, shards, the constellation's force layout: plain `System.Numerics`, stepped by the App | — | `Spring`, `ParticleSystem`, `ShardField`, `Constellation` |
| **Settings & secrets** | `PoVoiceHud.Core.Config` | `settings.json` in `%LOCALAPPDATA%\PoVoiceHud`, Credential Manager read/write | — | `ISettings`, `ISecretStore` |
| **Pipeline host** | `PoVoiceHud.Core` | Wires Audio → Vad → Asr → Router → Insights; exposes observable streams | all above | `HudPipeline` (`Transcript`, `Insights`, `Status` observables) |
| **HUD** | `PoVoiceHud.App` | Always-on-top acrylic window sized to its content, pill + cards (👍/👎, dismiss, You/Them) + transcript line + recap, settings flyout (applies without restart), global hotkeys, tray icon and menu, `vscode://` launch, docking | WinAppSDK, WinUIEx, Core | XAML + ViewModels |
| **Effects** | `PoVoiceHud.App` (`Fx/`, `Views/ConstellationView`) | ComputeSharp HLSL shaders drawn by Win2D (back swap chain, front overlay), Composition motion, the frame timer, the effects level policy, sound cues (`Core.Audio.SoundPalette`) | Win2D, ComputeSharp.D2D1.WinUI, Core.Fx | `HudEffects.Apply(EffectsLevel, sounds)` |
| **Install & update** | `PoVoiceHud.App` (`Program`, `Updates`, `Interop/Autostart`) | Velopack hooks in `Main`, background update from `Settings.UpdateFeed` applied on exit, HKCU Run entry; `build/pack.ps1` builds Setup.exe | Velopack | — |
