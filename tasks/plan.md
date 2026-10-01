# PoVoiceHud — Implementation Plan

Approved 2026-10-01. Companion checklist: `tasks/todo.md`. Spec: `SPEC.md`.

## Tooling (final)

Pinned in SPEC §3 plus: WinUIEx 2.9.3 · Humanizer.Core 3.0.10 · Microsoft.Extensions.DependencyInjection 10.0.x · Microsoft.Extensions.Logging 10.0.x (+ .Debug) · Raffinert.FuzzySharp 2.0.3 · Polly.Core 8.7.0 · Verify.XunitV3 31.25.0 · xunit.v3 4.0.1 · NSubstitute 6.2.0 · coverlet.collector · CommunityToolkit.Mvvm 8.4.2.
Declined: CsWin32 (two `[LibraryImport]`s instead), Meziantou CredentialManager (`CredRead/CredWrite` P/Invoke; `PasswordVault` needs package identity), CommunityToolkit.WinUI.Controls.

## Architecture decisions

| # | Decision | Why |
|---|---|---|
| A1 | `Core` is `net10.0-windows10.0.26100.0` class lib with **no WinUI reference**; `App` references Core; Tests reference Core only | Everything but XAML is testable with plain `dotnet test` |
| A2 | Audio graph in NAudio: `WasapiLoopbackCapture` + `WasapiCapture` → `BufferedWaveProvider` each → `ToSampleProvider()` → `MixingSampleProvider` → `WdlResamplingSampleProvider(16000)` → mono → float→int16 → `PcmFrame(1600 samples = 100 ms)` | Spec chain; one timer thread pulls the mixer so loopback silence gaps are padded (loopback `DataAvailable` stops when nothing plays) |
| A3 | Speaker hint = per-source RMS over the same 100 ms window (`Teams` if loopback RMS > mic RMS × 1.5, else `Mic`) | Cheap, deterministic; no diarization |
| A4 | VAD: Silero v6 ONNX, CPU EP, 512-sample frames, state `[2,1,128]` float32, `sr=16000` int64; segmenter is a pure state machine over `(frameProb, timestamp)` | Model-free unit tests |
| A5 | Segmenter emits when silence ≥ 650 ms after ≥ 300 ms of speech; hard cut at 30 s; 300 ms pre-roll; 60 s `RingBuffer<short>` owns audio | Whisper 30 s window; pre-roll avoids clipped first syllable |
| A6 | Whisper: `WhisperFactory.FromPath(model)` once; one `WhisperProcessor` per utterance via `CreateBuilder().WithLanguage("en").WithThreads(n).Build()`; input `float[]` | Avoids WAV re-encoding; thread count tunable for CPU budget |
| A7 | Router = `NoiseFilter` → `IntentClassifier` (keyword table → `Intent` flags) → `SymbolMatcher` (FuzzySharp `TokenSetRatio ≥ 80` vs `SymbolIndex`) → fan-out to providers whose `Handles` matches → rank by confidence | Spec §3.3; sub-10 ms |
| A8 | `SymbolIndex` built from tracked file paths, EF entity/table names, ADR titles/headings; refreshed by a debounced `FileSystemWatcher` | One index serves all providers |
| A9 | Providers return `Insight?`; each has a `Fake*` used in tests **and** by DI when unconfigured | "Run end-to-end with mock fallbacks" |
| A10 | AzDO through `IBuildClient` wrapping `BuildHttpClient`; Brave through `HttpClient`; both behind Polly retry (3 × exponential, 429/5xx only) | Testable without network |
| A11 | Secrets: `CredentialStore` (`CredWriteW/CredReadW/CredDeleteW`, `CRED_TYPE_GENERIC`, targets `PoVoiceHud/AzDoPat`, `PoVoiceHud/BraveKey`) | Spec §8 |
| A12 | `HudPipeline` exposes three `Channel<T>` readers (`Transcript`, `Insights`, `Status`); App drains them on `DispatcherQueue` | stdlib only |
| A13 | Window: `WinUIEx.WindowEx` with `IsAlwaysOnTop`, `IsTitleBarVisible=false`, `DesktopAcrylicBackdrop`, `PersistenceId="Hud"`, `MinWidth=420`; snap on `PositionChanged` within 24 px of an edge | Replaces AppWindow glue |
| A14 | Hotkey: `RegisterHotKey(hwnd, 1, MOD_WIN\|MOD_SHIFT\|MOD_NOREPEAT, 'M')`; handled via `WindowManager.WindowMessageReceived` (`WM_HOTKEY=0x0312`) | No subclassing |
| A15 | Deep link: `Launcher.LaunchUriAsync(new Uri($"vscode://file/{path}:{line}"))`, forward-slashed path | Works unpackaged |
| A16 | DI: single `ServiceCollection` in `App.xaml.cs`; Core exposes `AddPoVoiceHudCore(...)` | One composition root |
| A17 | First-run `ModelStore.EnsureAsync(IProgress<double>)` → `WhisperGgmlDownloader` (base.en) + raw GitHub URL for `silero_vad.onnx`; verified by size | Spec journey 1 |

## Dependency graph

```
T01 scaffold ─┬─► T02 app shell (risk R1 retired here)
              ├─► T03 settings ─► T04 credentials
              ├─► T05 pcm/ring ─► T06 wasapi capture
              │                 └► T08 segmenter ◄─ T07 silero vad
              │                       └► T09 whisper + model store
              ├─► T10 routing ─► T12 insight/router ─► T18 pipeline
              └─► T11 workspace/index ─┬─► T13 git ──┐
                                       ├─► T14 schema ─┤
                                       └─► T15 docs ───┼─► T18 pipeline ─► T19 zones UI ─► T20 hotkey/jump/dock ─► T21 settings UI ─► T22 docs/perf
                 T04 ─► T16 azdo ──────────────────────┤
                 T04 ─► T17 brave ─────────────────────┘
```

## Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| R1 | WinUI XAML compiler fails under VS Build Tools 2022 (no full VS; `winapp` CLI and WinUI templates not installed) | Med | T02 hand-writes the csproj; fallback: WinAppSDK 1.8.x → `winget install Microsoft.WinAppCLI` → add WinUI workload to Build Tools (ask first) |
| R2 | base.en on CPU misses 450 ms | Med | Measured in T09; `WithThreads` tuning; offer `tiny.en`; report honestly |
| R3 | Loopback `DataAvailable` silent when no app plays | High | A2 timer-pull pads zeros; tested with fake providers |
| R4 | Loopback is 48 kHz float stereo; mic may be 16-bit 44.1 kHz | High | Both go through `ToSampleProvider()` + resampler; T06 test |
| R5 | Teams uses exclusive-mode or a virtual device | Low | Status-bar hint; mic path still works |
| R6 | TFS client preview pulls heavy transitive deps | Med | Isolated behind `IBuildClient`; fallback to raw REST via `HttpClient` (ask first) |
| R7 | Brave free tier needs card; 429s | Low | Degrade to `[Search]` link card |
| R8 | `RegisterHotKey` Win+Shift+M conflict | Low | Status warning; click-to-mute |
| R9 | WinUIEx 2.9.3 (net8.0) vs WinAppSDK 2.5.1 compatibility | Med | Checked in T02; fallback to AppWindow/OverlappedPresenter |
| R10 | Memory > 450 MB with Roslyn + ONNX + Whisper | Med | Single `CSharpSyntaxTree` parse, no `Compilation`; measured in T22 |

## Checkpoints

- **CP1 after T02** — `dotnet test` green; WinUI window opens always-on-top from a CLI build. Retires R1/R9.
- **CP2 after T06** — live capture writes a 10 s mixed 16 kHz WAV (manual) + mixing tests green.
- **CP3 after T09** — fixture WAV → transcript; criterion 3 timing recorded.
- **CP4 after T12** — router tests green; fakes produce cards end-to-end.
- **CP5 after T17** — all five providers tested; AzDO live query manual check.
- **CP6 after T21** — full HUD running; manual Teams checklist.
- **CP7 after T22** — Phase 5 (code-review, security-review, simplify, evidence).

## Implementation examples (selected libraries)

1. **WinUIEx window** — `public sealed partial class MainWindow : WindowEx { public MainWindow() { InitializeComponent(); IsAlwaysOnTop = true; IsTitleBarVisible = false; PersistenceId = "Hud"; MinWidth = 420; SystemBackdrop = new DesktopAcrylicBackdrop(); } }`
2. **WM_HOTKEY via WinUIEx** — `WindowManager.Get(this).WindowMessageReceived += (_, e) => { if (e.Message.MessageId == 0x0312) pipeline.ToggleMute(); };` + `[LibraryImport("user32.dll")] static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);` (`MOD_WIN=8 | MOD_SHIFT=4 | MOD_NOREPEAT=0x4000`, vk `'M'`).
3. **NAudio mix graph** — `var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true }; mixer.AddMixerInput(loopBuf.ToSampleProvider()); mixer.AddMixerInput(micBuf.ToSampleProvider().ToStereo()); var mono = new WdlResamplingSampleProvider(mixer.ToMono(), 16000);` then a 100 ms timer `mono.Read(buf, 0, 1600)`.
4. **Silero ONNX call** — inputs `input [1,512] float`, `state [2,1,128] float`, `sr [1] int64 = 16000`; outputs `output` (prob), `stateN` → feed back as `state`.
5. **Whisper.net** — `_factory = WhisperFactory.FromPath(modelPath); using var p = _factory.CreateBuilder().WithLanguage("en").WithThreads(n).Build(); await foreach (var s in p.ProcessAsync(floatSamples, ct)) sb.Append(s.Text);`
6. **ModelStore download** — `await using var src = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.BaseEn); await using var dst = File.Create(tmp); await src.CopyToAsync(dst);` then `File.Move(tmp, final, overwrite: true)`.
7. **FuzzySharp symbol match** — `var best = Process.ExtractOne(phrase, index.Names, scorer: ScorerCache.Get<TokenSetScorer>()); if (best.Score >= 80) return index[best.Value];`
8. **LibGit2Sharp history** — `repo.Commits.QueryBy(relPath).Take(5).Select(e => new CommitInfo(e.Commit.Sha[..7], e.Commit.Author.Name, e.Commit.Author.When.Humanize(), e.Commit.MessageShort))`.
9. **Roslyn EF snapshot walk** — `CSharpSyntaxTree.ParseText(src).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()`; match `modelBuilder.Entity("Ns.Inspection", b => …)` lambdas; inside, `b.Property<Guid>("TenantId").IsRequired()`, `b.HasIndex("TenantId")`, `b.HasOne(...).WithMany(...).HasForeignKey("TenantId")`; line = `node.GetLocation().GetLineSpan().StartLinePosition.Line + 1`.
10. **Polly retry** — `new ResiliencePipelineBuilder().AddRetry(new() { MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(e => e.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError) }).Build()`.

Also: Markdig `Markdown.Parse(text).Descendants<HeadingBlock>()` for ADR title/sections; `Verify.XunitV3` `await Verify(parser.Parse(fixture))` for schema/ADR models.
