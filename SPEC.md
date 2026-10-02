# PoVoiceHud — Specification

Version 1.0 · 2026-10-01 · Status: **awaiting approval**

## 1. Objective

An offline-first, always-on-top Windows 11 HUD that listens to a Teams call (system loopback) and the local mic, transcribes locally with Whisper, detects technical questions, and answers them in one sentence from the active repository (git history, EF Core schema, `/docs` ADRs, Azure DevOps pipelines) with a `[Jump]` into VS Code. Audio never leaves the machine; the only outbound traffic is the optional AzDO query and the optional Brave web fallback.

## 1a. Modes and toggles (change request, 2026-10-01)

PoVoiceHud has two modes, switchable in Settings and persisted:

| | **Simple mode (default)** | **Repo mode** |
|---|---|---|
| Audio input | `Mic` only — the person at the laptop | `Both` (mic + remote loopback); `Remote` only also available |
| What is routed | Declarative statements (≥ 4 words, not a question) → **FactCheck** | Questions → Git / Schema / Docs / Pipeline / Web by intent |
| Repo required | No | Yes (for Git/Schema/Docs) |

Every source is an individual on/off toggle: **Git history**, **Schema**, **Docs/ADR**, **Azure DevOps**, **Fact check**, plus the audio input selector (`Mic` / `Remote` / `Both`). Simple mode = `Mic` + Fact check on, everything else off; Repo mode = `Both` + the four repo sources on, Fact check off. Toggles can be mixed freely after choosing a mode. The Settings flyout shows only the fact-check model at the top; everything repo-related sits under a collapsed **Advanced** section.

**FactCheck provider:** each statement goes to an LLM — the user's **Azure AI Foundry** project (serverless chat-completions endpoint, model configurable, default `Llama-3.3-70B-Instruct`) or a local Ollama model — which returns a JSON verdict `{verdict: supported|contradicted|unverified|not_a_claim, reason, source}` from its own knowledge. The card shows `✅ Supported` / `❌ Contradicted` / `⚠️ Unverified` + one-sentence reason (+ a link when the model names an http(s) source); `not_a_claim` (commands, chatter, questions, fragments) produces no card. Without an endpoint, the card says `⚠️ Unverified (no LLM configured)`. **Web search was removed by decision on 2026-10-01** (no Brave/Google keys; no `[WEB]` answer cards). **Privacy:** in Simple mode only the spoken statement leaves the machine, to the user's own Azure tenant or localhost; audio never does. The Whisper model is fixed to `base.en` (still overridable in `settings.json`).

## 2. User journeys

1. **First run** — launch `PoVoiceHud.exe`; pick a repo root; the app downloads `ggml-base.en.bin` and `silero_vad.onnx` to `%LOCALAPPDATA%\PoVoiceHud\models` with a progress bar; status bar shows `● REC | Repo: <path>`.
2. **Schema question in a call** — a participant says "Does the Inspection table have a TenantId foreign key?" → within 1.2 s Zone 1 shows `[SCHEMA] Inspection.TenantId` / "Yes. Required Guid FK → Tenants.Id, indexed." / `[Jump] src/.../AppDbContextModelSnapshot.cs:142`. Clicking Jump focuses VS Code on that line.
3. **Git question** — "who last touched the invoice service?" → `[GIT]` card: `a1b2c3d · Jane Doe · 3 days ago · "Fix rounding"` + Jump to file.
4. **ADR question** — "why did we use a claims header for auth?" → `[DOCS]` card with ADR title, status, decision sentence, and `⚠️ STALE` if code under the documented directory changed >14 days after the doc.
5. **CI question** — "did the build pass on the feature branch?" → `[AZDO]` card: last run status/result/duration, failing stage if any, link to the run. Shows "AzDO not configured" card if no PAT.
6. **Unknown library question** — "what does `IAsyncDisposable` do again?" → `[WEB]` card with Brave's top result title + description + link; or, without a key, a `[Search]` button that opens the browser.
7. **Mute** — press Win+Shift+M anywhere: capture stops, buffers and queues are flushed, REC dot turns red. Press again to resume.
8. **Device switch** — unplug the headset mid-call: the app re-binds to the new default render/capture endpoints within 2 s without restart.
9. **Reposition** — drag the HUD; it snaps to screen edges/corners; position is remembered.
10. **Fact check (Simple mode)** — you say "The Eiffel Tower was completed in 1889" → within ~3 s a `[FACT]` card: `✅ Supported — Completed in March 1889 for the Exposition Universelle.` + link to the source. "Python was created by Dennis Ritchie" → `❌ Contradicted — Python was created by Guido van Rossum; Ritchie created C.`
11. **Switch mode** — Settings → Repo mode: pick a repo; audio switches to `Both`; questions start producing repo cards.

## 3. Pinned stack

| Concern | Package | Version |
|---|---|---|
| Runtime / TFM | .NET SDK 10.0.400 · `net10.0-windows10.0.26100.0` · `LangVersion=latest` | — |
| UI | `Microsoft.WindowsAppSDK` (WinUI 3), unpackaged, `WindowsAppSDKSelfContained=true` | 2.5.1 |
| Audio | `NAudio.Wasapi`, `NAudio.Core` | 2.3.0 |
| VAD | `Microsoft.ML.OnnxRuntime` (CPU) + Silero VAD `silero_vad.onnx` (v6, MIT) | 1.30.0 |
| ASR | `Whisper.net`, `Whisper.net.Runtime` (CPU), model `ggml-base.en.bin` | 1.9.1 |
| Git | `LibGit2Sharp` | 0.32.0 |
| Schema | `Microsoft.CodeAnalysis.CSharp` | 5.9.0 |
| Docs | `Markdig` | 1.4.0 |
| CI | `Microsoft.TeamFoundationServer.Client` | 20.279.0-preview |
| Web | Brave Search API `GET https://api.search.brave.com/res/v1/web/search` (`X-Subscription-Token`) | — |
| Fact-check LLM | Azure AI Foundry serverless chat completions (OpenAI-compatible `POST {endpoint}/models/chat/completions`, `api-key` header, `response_format: json_object`), plain `HttpClient` — no SDK | api-version pinned in `FoundryClient` |
| MVVM | `CommunityToolkit.Mvvm` | 8.4.2 |
| Windowing | `WinUIEx` | 2.9.3 |
| Helpers | `Humanizer.Core` 3.0.10 · `Raffinert.FuzzySharp` 2.0.3 · `Polly.Core` 8.7.0 · `Microsoft.Extensions.DependencyInjection` / `.Logging` / `.Logging.Debug` 10.0.x | — |
| Tests | `xunit.v3` 4.0.1 (Microsoft.Testing.Platform runner) · `Microsoft.Testing.Extensions.CodeCoverage` 18.11.2 · `NSubstitute` 6.2.0 · `Verify.XunitV3` 31.25.0 | pinned in `Directory.Packages.props` |

Not used (decided): DirectML, CUDA, Vulkan, Radzen/Blazor, Ollama/LLMs, MSIX, CsWin32, Meziantou CredentialManager, `PasswordVault` (needs package identity).

## 4. Commands

```
dotnet build PoVoiceHud.sln -c Debug
dotnet test  --solution PoVoiceHud.sln                 # MTP mode (global.json "test.runner")
dotnet test  --solution PoVoiceHud.sln --coverage      # Microsoft.Testing.Extensions.CodeCoverage
dotnet format PoVoiceHud.sln --verify-no-changes      # lint
dotnet run   --project src/PoVoiceHud.App
dotnet publish src/PoVoiceHud.App -c Release -r win-x64 --self-contained
```

## 5. Project structure

```
PoVoiceHud.sln
Directory.Build.props            # nullable, implicit usings, warnings-as-errors, analyzers
Directory.Packages.props         # central package versions
src/
  PoVoiceHud.Core/
    Audio/        WasapiDualCapture.cs, PcmFrame.cs, IAudioSource.cs
    Vad/          SileroVad.cs, UtteranceSegmenter.cs, RingBuffer.cs
    Asr/          WhisperTranscriber.cs, ModelStore.cs
    Routing/      Router.cs, Intent.cs, NoiseFilter.cs, SymbolMatcher.cs
    Workspace/    Workspace.cs, SymbolIndex.cs
    Providers/    IInsightProvider.cs, Insight.cs,
                  Git/GitProvider.cs, Schema/EfSnapshotParser.cs, Schema/SchemaProvider.cs,
                  Docs/DocsProvider.cs, Pipeline/AzdoProvider.cs, Web/BraveProvider.cs,
                  Fakes/*.cs
    Config/       Settings.cs, CredentialStore.cs
    HudPipeline.cs
  PoVoiceHud.App/
    App.xaml(.cs), MainWindow.xaml(.cs)
    Views/        FocusCard.xaml, ShelfCard.xaml, TranscriptStrip.xaml, StatusBar.xaml, SettingsFlyout.xaml
    ViewModels/   HudViewModel.cs, InsightCardViewModel.cs, SettingsViewModel.cs
    Interop/      GlobalHotkey.cs, WindowDocking.cs, VsCodeLauncher.cs
tests/
  PoVoiceHud.Core.Tests/
    Fixtures/     *.wav (2–5 s utterances), sample-repo/ (git repo w/ EF snapshot + docs/adr)
    Audio/ Vad/ Asr/ Routing/ Providers/ Config/
docs/adr/                        # this app's own ADRs (also exercises the Docs provider)
tasks/plan.md, tasks/todo.md
```

## 5a. HUD layout (Phase 3, concept 06 "Pill and expand")

At rest the HUD is a one-line pill (~44 px): REC indicator · latest headline · expand toggle · settings. A new insight with confidence ≥ 0.6 auto-expands the full stack (status bar, Zone 1 focus card, Zone 2 shelf of ≤6, Zone 3 transcript of 8 lines) for 20 s, then collapses unless the pointer is over it; clicking the pill pins it open until clicked again. The window resizes between the two states.

Hierarchy: `MainWindow` → `PillBar` + `ExpandedPanel` (`StatusBar`, `FocusCard`, `InsightShelf`→`ShelfCard`, `TranscriptStrip`), plus `SettingsFlyout`, `FirstRunDialog`. ViewModels: `HudViewModel`, `InsightCardViewModel`, `SettingsViewModel`.

## 6. Code style

```csharp
// file-scoped namespaces, primary constructors, records for payloads, no regions
namespace PoVoiceHud.Core.Providers;

public sealed record Insight(Intent Intent, string Headline, string Answer, IReadOnlyList<Jump> Jumps, Staleness? Stale = null);
public sealed record Jump(string AbsolutePath, int Line) { public Uri Uri => new($"vscode://file/{AbsolutePath.Replace('\\', '/')}:{Line}"); }

public interface IInsightProvider
{
    Intent Handles { get; }
    Task<Insight?> QueryAsync(Query query, CancellationToken ct);
}
```

- `TreatWarningsAsErrors=true`, `Nullable=enable`, `dotnet format` clean.
- One public type per file. Async all the way; no `.Result`.
- Ponytail rules: stdlib first, no abstraction without two callers, no config for things that don't vary.
- Comments only where the *why* is non-obvious (e.g. Silero state shape, WASAPI buffer math).

## 7. Testing strategy

| Level | Scope | Tooling |
|---|---|---|
| Unit | Router, NoiseFilter, SymbolMatcher, EfSnapshotParser, DocsProvider freshness math, Settings, RingBuffer, PCM framing | xUnit v3, plain `Assert` |
| Component | VAD segmentation over fixture WAVs (silence gaps 300/650/900 ms); Whisper on a fixture WAV (asserts key tokens, not exact text); GitProvider on a temp repo built with LibGit2Sharp | xUnit v3, `[Trait("Category","Model")]` for tests needing downloaded models |
| Integration | `HudPipeline` end-to-end with `FakeAudioSource` replaying a WAV → asserts an `Insight` arrives | xUnit v3 |
| Manual | Live Teams call checklist in `docs/manual-test.md` | — |

Coverage target: **≥80 % line coverage on `PoVoiceHud.Core`**, measured with `dotnet test --coverage` (Microsoft.Testing.Extensions.CodeCoverage; VSTest/coverlet is unsupported on the .NET 10 SDK with xunit.v3 4.x). App project is excluded (ViewModels are thin; tested via Core contracts). TDD: every task starts red.

## 8. Boundaries

**Always:** run `dotnet test` before every commit · keep audio on-host · honour mute within 50 ms · degrade to fakes when models/keys are missing · one commit per task.

**Ask first:** adding a NuGet package not in §3 · any new outbound HTTP endpoint · changing latency/memory budgets · deleting fixtures · touching Credential Manager entries other than `PoVoiceHud/*`.

**Never:** commit secrets or model binaries · send audio or transcript text to any network service other than Brave (only the extracted query string) · weaken or skip failing tests · add Blazor/Radzen/LLM dependencies.

## 9. Out of scope (v1)

Speaker diarization · non-English models · multi-repo · MSIX/Store · GitHub Actions/GitLab providers · LLM-generated answers for repo intents (the LLM is used only for fact-check verdicts) · transcript persistence · VS Code extension · tray icon · auto-update.

## 10. Edge cases & error states

| Situation | Behaviour |
|---|---|
| No default render/capture device | Status bar `⚠ No audio device`; pipeline idle; retries on `IMMNotificationClient` default-change |
| Device changes mid-session | Stop, dispose, re-create captures; drop ≤2 s audio; log once |
| Loopback delivers silence (exclusive-mode app) | VAD never fires; mic path still works |
| Model download fails / offline | Modal with retry; app runs with `FakeTranscriber` (shows "ASR unavailable") |
| Whisper output empty or `[BLANK_AUDIO]` | Dropped before router |
| Utterance > 30 s | Force-cut at 30 s (Whisper window), continue segment |
| Repo path missing/not a git repo | Settings flyout opens with validation message; providers disabled |
| EF snapshot absent | Schema provider returns null → router skips intent |
| Doc has no git history (untracked) | Freshness unknown → no stale flag |
| AzDO 401/403 | Card "AzDO auth failed — check PAT"; retry after 60 s |
| AzDO no runs for branch | Card "No runs for `<branch>`" |
| Brave 429/5xx | Fall back to `[Search]` link card |
| Hotkey already registered by another app | Status bar warning; mute via click still works |
| VS Code not installed | `LaunchUriAsync` returns false → toast "VS Code not found" |
| Two insights for one utterance | Highest-confidence in Zone 1, rest pushed to Zone 2 shelf (max 6, FIFO) |

## 11. Success criteria (measurable)

1. `dotnet build` and `dotnet test` succeed on a clean clone with no models or keys present; ≥80 % Core coverage.
2. Fixture WAV with 650 ms pauses yields exactly the expected number of utterances (±0) in the VAD test; 300 ms pauses yield one.
3. Whisper on `fixtures/tenant-question.wav` (base.en, CPU) completes in **≤450 ms** median over 5 runs on the dev machine, and output contains "tenant".
4. End-to-end (`FakeAudioSource` replay → Insight) completes in **≤1,200 ms** median.
5. Schema provider answers the nullability/FK/index question correctly for the fixture snapshot (exact `Answer` string asserted).
6. Git provider returns the 5 most recent commits for a file with correct SHA7/author/relative time.
7. Docs provider flags STALE when code commit date > doc date + 14 d, and not otherwise (both asserted).
8. AzDO provider parses a recorded JSON response into status/result/duration/stage/url (fake client); live query verified manually against your org.
9. Brave provider parses a recorded response; with no key the Web intent yields a `[Search]` link insight.
10. Working set **≤450 MB** and CPU **≤6 %** (24-core, average over 60 s of streaming) measured with `Get-Process`/PerfMon, recorded in `docs/perf.md`.
11. Mute hotkey clears queues: a test on `HudPipeline` proves no Insight is emitted from audio queued before `Mute()`.
12. Manual: live Teams call — question in each of the 5 intents produces a correct card; every `[Jump]` opens VS Code at the right line (checklist signed off in `docs/manual-test.md`).
13. Fact check: with a fake search + fake LLM, "supported"/"contradicted"/"unverified" verdicts render the three card forms; the Foundry client parses a recorded chat-completions response; no key → `⚠️ Unverified (no LLM configured)` with the top search result.
14. Toggles: with Fact check off and Git on, a statement produces no card and a git question does; `Mic` input never opens the loopback device (unit-tested on the capture's device selection).
15. Simple mode is the default on first run: no repo prompt, status bar shows `Mic · Fact check`.

## 12. Open questions

1. Whether the `Microsoft.WindowsAppSDK` 2.5.1 XAML compiler builds under VS Build Tools 2022 (17.14) without full VS — will be verified in the first scaffold task; fallback is WinAppSDK 1.8.x.
2. Exact `Whisper.net` CPU timings for base.en on this CPU — criterion 3 may need `small.en` ruled out / `tiny.en` offered if it misses.
3. Does your AzDO project use YAML pipelines only (Build API) or also classic Releases (Release API)? Spec assumes **Build API only**.
