# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

@AGENTS.md

PoVoiceHud is an always-on-top WinUI 3 HUD (not a web app): it transcribes what is said with Whisper and fact-checks each claim as it goes, highlighting the sentence green or red. `README.md` is the one document: use, settings, files, and how it works with the design decisions behind it. `docs/manual-test.md` is the checklist for what tests cannot reach.

## Repo facts

- The only branch is `main`. Where `AGENTS.md` says "master", that means `main`.
- Commits stay local: no remote is used for this project unless the owner says otherwise.
- `src/PoVoiceHud.Core` holds everything testable (audio, VAD, ASR, the fact checker, view models) and never references WinUI. `src/PoVoiceHud.App` is the XAML shell. `tests/PoVoiceHud.Core.Tests` is xUnit v3 on Microsoft.Testing.Platform.
- Build output is in `artifacts/`, not `bin/` and `obj/`.
- Warnings are errors, with analyzers at `latest-recommended`. Rules that are switched off are listed with their reason in `.editorconfig`; add to that list rather than suppressing inline.

## Commands

```powershell
dotnet build PoVoiceHud.sln
dotnet test --solution PoVoiceHud.sln                                      # everything, about 10 s
dotnet test --project tests/PoVoiceHud.Core.Tests -- --filter-class "*FactCheckerTests"   # one class
dotnet run --project src/PoVoiceHud.App                                    # the real app: opens the microphone
dotnet run --project src/PoVoiceHud.App -- --demo                          # a sample conversation, no audio (Debug)
dotnet run --project src/PoVoiceHud.App -- --demo --facts                  # the same, on the Facts page (Debug)
dotnet run --project src/PoVoiceHud.App -- --demo --settings               # the settings layout, no audio (Debug)
pwsh build/pack.ps1                                                        # Setup.exe + update packages in artifacts/releases (Velopack)
```

## How a sentence becomes a highlight

- **Pipeline** (`HudPipeline`): audio frames → `SileroVad` + `UtteranceSegmenter` → Whisper → `Sentences` (one line each) → `FactChecker`. Transcription and checking are two separate channel stages, so a slow model call never delays the next line. `Checking` says when a line is queued for the checker and when it is done; `Verdicts` carries each verdict with its line. Muting increments a generation counter; work already in flight is thrown away when it finishes.
- **FactChecker**: `IsCandidate` (word lists: three real words, not fillers, not a question or request) → Wikipedia evidence → `FoundryClient` → a verdict, or null for `not_a_claim`. Every failure is a "can't tell" that says why. `FoundryClient.Model` is fixed (`gpt-4.1-mini`) and the reply is held to a JSON schema.
- **Words**: the `Verdict` enum keeps the model's terms (Supported, Contradicted, Unverified); everything a person reads uses `Verdict.Label()`: Correct, Incorrect, Can't tell.
- **View models**: `HudViewModel` holds `Live` (`LiveLineViewModel`: Heard → Checking → Correct / Incorrect / CantTell) and `Facts` (`FactViewModel`, correct and incorrect only, newest first, repeats counted). The window is a standard one (title bar, resizable, kept on top); the pages fill it.
- **Wiring** is all in `ServiceCollectionExtensions.AddPoVoiceHudCore`. Without an endpoint and key the checker still runs and says what to set up.
- **Settings changes** (`App.ApplyAsync`): the whole DI container is disposed and `StartAsync` builds a new one, unless only Sounds or Start with Windows changed. A run counter stops a slower, older start from installing its pipeline. The only state is `settings.json` and Credential Manager (`CredentialStore`); for secrets, follow the existing Credential Manager pattern.
- **Feedback**: `SoundPalette` cues (each ≤ 240 ms, under the segmenter's 300 ms floor) and a `BrushTransition` on the highlight, both under the one *Sounds and animations* switch. There are no shaders or particle effects any more.

## Checking a UI change

The app is a desktop window, so the browser tools cannot drive it, and a settings flyout only opens for the foreground window. Use the `--demo` forms above and capture the window; do not send synthetic keys or clicks to the desktop, because the owner is usually working on it. Running without `--demo` opens the microphone and appends what it hears to `%LOCALAPPDATA%\PoVoiceHud\sessions`.

## Limits worth knowing

- 100 unit tests and 50 integration tests at most (`[Trait("Category", "Integration")]` marks the latter). The suite is at 61 and 13.
