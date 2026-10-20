# PoVoiceHud

An always-on-top Windows 11 HUD that listens, transcribes locally with Whisper, and answers in one sentence.

- **Simple mode (default):** your mic only. Declarative statements you say are looked up on Wikipedia and judged by a Llama/cheap model on your Azure AI Foundry project (or local Ollama): `✅ Supported` / `❌ Contradicted` / `⚠️ Unverified` + reason, linking the article it used. Commands, chatter and questions produce no card.
- **Repo mode (on whenever a repository is set in Settings):** everything Simple mode does, plus the remote call audio and a repository. Questions are answered from it — git history, EF Core schema, ADRs (with staleness check), Azure DevOps pipelines — and a sentence that names a file or entity gets a `[CODE]` card: the model reads those files and says what the code does about it. Cards link into VS Code.

Audio never leaves the machine. Only the spoken text goes to the LLM endpoint you configure, plus the key words of each claim to Wikipedia's search (switch that off in Settings).

## Install

```powershell
pwsh build/pack.ps1        # → artifacts/releases/PoVoiceHud.App-win-Setup.exe (+ update packages)
```

Setup.exe installs per user (no admin) to `%LOCALAPPDATA%\PoVoiceHud.App` and adds a Start menu shortcut. It is not code-signed, so SmartScreen warns the first time (**More info → Run anyway**). Settings, models and session logs stay in `%LOCALAPPDATA%\PoVoiceHud`, so uninstalling or updating keeps them. Raise `<Version>` in `src/PoVoiceHud.App/PoVoiceHud.App.csproj` before packing a new release. Keep `artifacts/releases` between versions, because Velopack builds small delta updates from the previous release.

**Updates:** set `UpdateFeed` in `settings.json` to an https URL or a folder (a network share works) and publish to it with `pwsh build/pack.ps1 -Feed <that folder>`. An installed copy checks the feed at start, downloads a newer version in the background and installs it when the HUD closes. **Check for updates** is also in the tray menu. Copies started with `dotnet run` or from `artifacts/` never update themselves.

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
| Fact-check provider and model, the optional repository (an Azure DevOps URL or a folder), *Look claims up on Wikipedia*, *Start with Windows*, *Sounds* (`Sounds`), *Effects* (`Effects`: `Full`, `Subtle` or `Off`) | `%LOCALAPPDATA%\PoVoiceHud\settings.json` |
| `Vocabulary` (comma-separated names Whisper should expect) and `UpdateFeed`: settings.json only, no UI | `%LOCALAPPDATA%\PoVoiceHud\settings.json` |
| Foundry/Ollama key, Azure DevOps PAT | Windows Credential Manager (`PoVoiceHud/FoundryKey`, `PoVoiceHud/AzDoPat`) |

- **Fact-check model:** once it works it is one line with a **Change…** button. Behind that, pick *Local Ollama* (only a model name is needed; nothing leaves the PC) or *Azure AI Foundry* (resource root `https://<resource>.services.ai.azure.com` + key; the app calls `/models/chat/completions?api-version=2024-05-01-preview`). **Test** sends one message with what is typed. On a first run with nothing configured, a running Ollama is picked up automatically.
- **Repository (optional):** leave it empty for Simple mode; fill it in for Repo mode. Either an Azure DevOps git URL (`https://dev.azure.com/org/project/_git/repo`, needs the access token) or a folder on this PC that holds a git repository. A URL is cloned into `%LOCALAPPDATA%\PoVoiceHud\repos` and refreshed on every start; a folder is read where it is. **Test repository** checks the connection with what is typed. The organization and project for pipeline status are read from the URL, or from the folder's `origin`.
- **What leaves the PC in Repo mode:** for a `[CODE]` card, the sentence and up to three matching files (first 6 000 characters each) go to the fact-check model you configured.
- **No Save button:** changes apply when the settings panel closes — the pipeline is rebuilt in place, no restart. If something typed is invalid, the pill says so and nothing is changed.
- **Mute:** Win+Shift+M, or the next of Ctrl+Shift+M, Ctrl+Alt+M, Ctrl+Shift+F12 if another program owns it (hover the microphone to see which is live), or click the microphone. Muting drops everything buffered. **Ctrl+Alt+R** checks the last sentence again. Click the pill (or **Ctrl+E**) to open or close the panel; **Esc** closes it.
- **Session log:** what was heard and answered, and your 👍/👎 on cards, is appended to `%LOCALAPPDATA%\PoVoiceHud\sessions\{date}.jsonl` on this PC.
- **Wikipedia lookups:** before the model judges a claim, its content words are searched on English Wikipedia (no key) and the top three article intros go to the model as evidence. A link to one of those articles is marked `(looked up)`; a URL the model wrote itself stays `(cited by the model)`. If the lookup fails or is slow (2.5 s limit), the model judges alone. See `docs/adr/0004-wikipedia-evidence.md`.
- **Who said it:** the transcript labels each line `You:` (the microphone) or `Them:` (the call). When call audio is on, cards carry the same badge.
- **Card actions:** 👍/👎 records whether the card was right (press again to take it back), ✕ puts the card away, and the copy button copies it. The tally's tooltip shows your ratings for the last 7 days.
- **Recap:** the **Recap** button next to the transcript, **Ctrl+Alt+S**, or the tray menu writes `sessions\{date}-recap-{time}.md`. It has a summary, decisions, action items and open questions (written by the fact-check model), then the fact checks, repository answers and transcript as they happened. It is also copied to the clipboard and shown as a card that opens the file. It covers the current conversation: everything since a pause of more than 20 minutes or since the last recap. When you mute after a conversation, the pill suggests one. With a cloud model, the transcript of that conversation is sent to it.
- **Whisper vocabulary:** Whisper is told the repository's names (EF entities, then the words that recur in file names) and any `Vocabulary` words from settings.json, so it writes `TenantId` rather than `tenant ID`. The list refreshes when the repository changes. It is kept to 120 characters, because a prompt adds ~80–100 ms per sentence and longer ones over-steer (`docs/perf.md`). Simple mode with no `Vocabulary` uses no prompt.
- **Tray icon:** a left click hides or shows the HUD. The right-click menu has mute, recap, the sessions folder, *Start with Windows*, *Check for updates* and quit.
- **Logs and diagnostics:** `%LOCALAPPDATA%\PoVoiceHud\logs\{date}.log` (kept 14 days) holds starts, settings changes, recaps, updates and every error. **Copy diagnostics** in Settings zips the last week of logs, `crash.log`, `settings.json` and a machine description into `%TEMP%`, puts the zip on the clipboard and shows it in Explorer. Session transcripts and keys are never included.
- **Local chatter filter:** when the fact-check model is in the cloud and Ollama is running, Ollama's first model sees each sentence first and drops the ones that are not claims, so they never leave the machine. It adds one local call per sentence, so a small model (`ollama pull llama3.2`) keeps it quick; if Ollama is not running the filter is skipped.
- **Effects and sounds:** *Full* (the default) gives the pill a frosted aurora that follows your voice, a voice ribbon in place of the meter bars, a shockwave in the verdict's colour when a card lands (red ones glitch), a comet round the pill while a sentence is checked, frost on mute that shatters on unmute, sparks for supported claims, 👍 and every tenth weekly 👍, cards that spring in and can be swiped away, and answers that sharpen into focus. *Subtle* keeps those moments without particles, shatter or glitch; *Off* is the plain HUD. Windows' *Animation effects* switch and high contrast turn effects off, and battery saver caps them at *Subtle*. Nothing is drawn while nothing moves. *Sounds* plays a short cue for each verdict, mute/unmute and ratings, each under 240 ms so call-audio capture never transcribes it. The ✦ button next to **Recap** shows the session as a constellation: one star per card, coloured by verdict, sized by confidence, with time running left to right and related claims linked. It also opens after a recap. Hover a star to read it, click it to bring the card back. See `docs/adr/0005-native-gpu-effects.md`.
- **Always on:** the transcript line and the choices each mode makes (Simple: mic + fact check; Repo: mic + call audio + git, schema, docs and pipelines). There are no switches for these.

## Test

```powershell
dotnet test --solution PoVoiceHud.sln               # 147 tests: 100 unit + 47 integration; only the model-timing ones run alone
dotnet test --solution PoVoiceHud.sln --coverage    # Microsoft.Testing.Extensions.CodeCoverage
```

Tests tagged `Category=Integration` use real git repositories, Credential Manager or the models; the model ones download Whisper and Silero once into `%LOCALAPPDATA%\PoVoiceHud\models`.

## Layout

`src/PoVoiceHud.Core` — audio, VAD, ASR, router, providers, session log and recap, file logging, view models (no WinUI). `src/PoVoiceHud.App` — WinUI 3 shell, tray icon, Velopack entry point and updates. `build/pack.ps1` — the installer. `tests/PoVoiceHud.Core.Tests` — xUnit v3. See `SPEC.md`, `CAPABILITY-MAP.md`, `docs/adr/`. Working rules for coding agents are in `AGENTS.md`.
