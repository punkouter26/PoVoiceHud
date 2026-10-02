# PoVoiceHud

An always-on-top Windows 11 HUD that listens, transcribes locally with Whisper, and answers in one sentence.

- **Simple mode (default):** your mic only. Declarative statements you say are judged by a Llama/cheap model on your Azure AI Foundry project (or local Ollama): `✅ Supported` / `❌ Contradicted` / `⚠️ Unverified` + reason. Commands, chatter and questions produce no card.
- **Repo mode (under Advanced in Settings):** mic + remote call audio. Questions are answered from the active repository — git history, EF Core schema, ADRs (with staleness check), Azure DevOps pipelines — with `[Jump]` buttons into VS Code.

Audio never leaves the machine. Only the spoken text goes to the LLM endpoint you configure.

## Run

```powershell
dotnet build PoVoiceHud.sln
dotnet run --project src/PoVoiceHud.App          # first run downloads ggml-base.en.bin (~148 MB) + silero_vad.onnx
dotnet run --project src/PoVoiceHud.App -- --pin   # start expanded (manual testing)
dotnet run --project src/PoVoiceHud.App -- --demo  # sample cards, no audio
```

Requirements: Windows 11, .NET 10 SDK, a microphone. No GPU needed (Whisper runs on CPU; base.en ≈ 300 ms per utterance on a modern laptop).

## Configure (⚙ in the pill)

| Setting | Where it lives |
|---|---|
| Mode, audio input, source toggles, repo path, endpoints, model names (and `WhisperModel`, fixed to `base.en` unless edited here) | `%LOCALAPPDATA%\PoVoiceHud\settings.json` |
| Foundry/Ollama key, Azure DevOps PAT | Windows Credential Manager (`PoVoiceHud/FoundryKey`, `PoVoiceHud/AzDoPat`) |

- **Fact-check model:** either an Azure AI Foundry resource root (`https://<resource>.services.ai.azure.com` + key; the app calls `/models/chat/completions?api-version=2024-05-01-preview`) or a full OpenAI-compatible URL such as Ollama's `http://localhost:11434/v1/chat/completions` (no key). "Use local Ollama" fills that in; `ollama pull llama3.2` first.
- **Mute:** Ctrl+Shift+M (Win+Shift+M when Explorer doesn't own it) or click the dot. Muting drops everything buffered.

## Test

```powershell
dotnet test --solution PoVoiceHud.sln               # 169 tests, sequential (timing assertions)
dotnet test --solution PoVoiceHud.sln --coverage    # Microsoft.Testing.Extensions.CodeCoverage
```

Tests tagged `Category=Model` download the Whisper and Silero models once into `%LOCALAPPDATA%\PoVoiceHud\models`.

## Layout

`src/PoVoiceHud.Core` — audio, VAD, ASR, router, providers, view models (no WinUI). `src/PoVoiceHud.App` — WinUI 3 shell. `tests/PoVoiceHud.Core.Tests` — xUnit v3. See `SPEC.md`, `CAPABILITY-MAP.md`, `tasks/plan.md`, `docs/adr/`.
