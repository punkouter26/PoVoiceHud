# PoVoiceHud

An always-on-top Windows 11 HUD that listens, transcribes locally with Whisper, and answers in one sentence.

- **Simple mode (default):** your mic only. Declarative statements you say are judged by a Llama/cheap model on your Azure AI Foundry project (or local Ollama): `✅ Supported` / `❌ Contradicted` / `⚠️ Unverified` + reason. Commands, chatter and questions produce no card.
- **Repo mode (on whenever a repository is set in Settings):** everything Simple mode does, plus the remote call audio and a repository. Questions are answered from it — git history, EF Core schema, ADRs (with staleness check), Azure DevOps pipelines — and a sentence that names a file or entity gets a `[CODE]` card: the model reads those files and says what the code does about it. Cards link into VS Code.

Audio never leaves the machine. Only the spoken text goes to the LLM endpoint you configure.

## Run

```powershell
dotnet build PoVoiceHud.sln
dotnet run --project src/PoVoiceHud.App          # first run downloads ggml-base.en.bin (~148 MB) + silero_vad.onnx
dotnet run --project src/PoVoiceHud.App -- --pin               # start expanded (Debug builds)
dotnet run --project src/PoVoiceHud.App -- --demo              # sample cards, no audio (Debug builds)
dotnet run --project src/PoVoiceHud.App -- --demo --settings   # the settings layout, no audio (Debug builds; add --all to unfold every field)
```

Build output goes to `artifacts/` (`artifacts/bin/PoVoiceHud.App/debug_win-x64/PoVoiceHud.App.exe`). Both model downloads are checked against a pinned SHA-256.

Requirements: Windows 11, .NET 10 SDK, a microphone. No GPU needed (Whisper runs on CPU; base.en ≈ 300 ms per utterance on a modern laptop).

## Configure (⚙ in the pill)

| Setting | Where it lives |
|---|---|
| Fact-check provider and model, and the optional repository (an Azure DevOps URL or a folder) | `%LOCALAPPDATA%\PoVoiceHud\settings.json` |
| Foundry/Ollama key, Azure DevOps PAT | Windows Credential Manager (`PoVoiceHud/FoundryKey`, `PoVoiceHud/AzDoPat`) |

- **Fact-check model:** once it works it is one line with a **Change…** button. Behind that, pick *Local Ollama* (only a model name is needed; nothing leaves the PC) or *Azure AI Foundry* (resource root `https://<resource>.services.ai.azure.com` + key; the app calls `/models/chat/completions?api-version=2024-05-01-preview`). **Test** sends one message with what is typed. On a first run with nothing configured, a running Ollama is picked up automatically.
- **Repository (optional):** leave it empty for Simple mode; fill it in for Repo mode. Either an Azure DevOps git URL (`https://dev.azure.com/org/project/_git/repo`, needs the access token) or a folder on this PC that holds a git repository. A URL is cloned into `%LOCALAPPDATA%\PoVoiceHud\repos` and refreshed on every start; a folder is read where it is. **Test repository** checks the connection with what is typed. The organization and project for pipeline status are read from the URL, or from the folder's `origin`.
- **What leaves the PC in Repo mode:** for a `[CODE]` card, the sentence and up to three matching files (first 6 000 characters each) go to the fact-check model you configured.
- **No Save button:** changes apply when the settings panel closes — the pipeline is rebuilt in place, no restart. If something typed is invalid, the pill says so and nothing is changed.
- **Mute:** Win+Shift+M, or the next of Ctrl+Shift+M, Ctrl+Alt+M, Ctrl+Shift+F12 if another program owns it (hover the microphone to see which is live), or click the microphone. Muting drops everything buffered. **Ctrl+Alt+R** checks the last sentence again. Click the pill (or **Ctrl+E**) to open or close the panel; **Esc** closes it.
- **Session log:** what was heard and answered is appended to `%LOCALAPPDATA%\PoVoiceHud\sessions\{date}.jsonl` on this PC.
- **Local chatter filter:** when the fact-check model is in the cloud and Ollama is running, Ollama's first model sees each sentence first and drops the ones that are not claims, so they never leave the machine. It adds one local call per sentence, so a small model (`ollama pull llama3.2`) keeps it quick; if Ollama is not running the filter is skipped.
- **Always on:** the transcript line, the tone on a contradicted statement, and the choices each mode makes (Simple: mic + fact check; Repo: mic + call audio + git, schema, docs and pipelines). There are no switches for these.

## Test

```powershell
dotnet test --solution PoVoiceHud.sln               # 146 tests: 100 unit + 46 integration; only the model-timing ones run alone
dotnet test --solution PoVoiceHud.sln --coverage    # Microsoft.Testing.Extensions.CodeCoverage
```

Tests tagged `Category=Integration` use real git repositories, Credential Manager or the models; the model ones download Whisper and Silero once into `%LOCALAPPDATA%\PoVoiceHud\models`.

## Layout

`src/PoVoiceHud.Core` — audio, VAD, ASR, router, providers, view models (no WinUI). `src/PoVoiceHud.App` — WinUI 3 shell. `tests/PoVoiceHud.Core.Tests` — xUnit v3. See `SPEC.md`, `CAPABILITY-MAP.md`, `docs/adr/`. Working rules for coding agents are in `AGENTS.md`.
