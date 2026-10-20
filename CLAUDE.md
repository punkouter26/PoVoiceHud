# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

@AGENTS.md

PoVoiceHud is an always-on-top WinUI 3 HUD (not a web app): it listens, transcribes locally with Whisper, and fact-checks or answers in one sentence. `README.md` has the user-facing overview; `SPEC.md`, `CAPABILITY-MAP.md` and `docs/` have the design.

## Repo facts

- The only branch is `main`. Where `AGENTS.md` says "master", that means `main`.
- Commits stay local: no remote is used for this project unless the owner says otherwise.
- `src/PoVoiceHud.Core` holds everything testable (audio, VAD, ASR, routing, providers, view models) and never references WinUI. `src/PoVoiceHud.App` is the XAML shell. `tests/PoVoiceHud.Core.Tests` is xUnit v3 on Microsoft.Testing.Platform.
- Build output is in `artifacts/`, not `bin/` and `obj/`.
- Warnings are errors, with analyzers at `latest-recommended`. Rules that are switched off are listed with their reason in `.editorconfig`; add to that list rather than suppressing inline.

## Commands

```powershell
dotnet build PoVoiceHud.sln
dotnet test --solution PoVoiceHud.sln                                      # everything, about 10 s
dotnet test --project tests/PoVoiceHud.Core.Tests -- --filter-class "*FactCheckProviderTests"   # one class
dotnet run --project src/PoVoiceHud.App                                    # the real app: opens the microphone
dotnet run --project src/PoVoiceHud.App -- --demo                          # sample cards, no audio (Debug)
dotnet run --project src/PoVoiceHud.App -- --demo --settings               # the settings layout, no audio (Debug; --all unfolds every field)
```

## How a sentence becomes a card

- **Pipeline** (`HudPipeline`): audio frames → `SileroVad` + `UtteranceSegmenter` → Whisper → `Router`. Transcription and routing are two separate channel stages, so a slow LLM call never delays the next transcription. Muting increments a generation counter; work already in flight is thrown away when it finishes.
- **Router**: `NoiseFilter` → `IntentClassifier` (keyword intents, masked by `Settings.EnabledSources`) → `SymbolMatcher` against the workspace index. A sentence that names a repository symbol and that no keyword source claimed goes to `Code`, not `FactCheck`. Other declarative sentences go to `FactCheck`. Matching providers run in parallel and their answers are ranked by confidence. A provider that throws is logged and skipped.
- **Providers** implement `IInsightProvider` (`Handles` is an `Intent` flag; return null for "nothing to say"). All wiring is in `ServiceCollectionExtensions.AddPoVoiceHudCore`. A provider that is missing its configuration is registered as `UnconfiguredProvider`, which says why, rather than being left out. `FoundryClient` talks to both Azure AI Foundry and Ollama; when the main model is in the cloud and Ollama is running, a local Ollama model screens out chatter first.
- **Modes** are presets in `Settings.ApplyMode`: Simple is mic + FactCheck; Repo is mic and call audio + every source.
- **Settings changes** (`App.ApplyAsync`): the whole DI container is disposed and `StartAsync` builds a new one. A run counter stops a slower, older start from installing its pipeline. The only state is `settings.json` and Credential Manager (`CredentialStore`); for secrets, follow the existing Credential Manager pattern.
- **Design decisions**, with their reasons, are in `docs/adr/` (CPU Whisper, a deterministic router rather than an LLM one, fact-checking on Foundry).

## Checking a UI change

The app is a desktop window, so the browser tools cannot drive it, and a settings flyout only opens for the foreground window. Use the two `--demo` forms above and capture the window; do not send synthetic keys or clicks to the desktop, because the owner is usually working on it. Running without `--demo` opens the microphone and appends what it hears to `%LOCALAPPDATA%\PoVoiceHud\sessions`.

## Limits worth knowing

- 100 unit tests and 50 integration tests at most (`[Trait("Category", "Integration")]` marks the latter). The suite is at 100 and 46.
- The Azure DevOps provider has never been run against a live organization.
