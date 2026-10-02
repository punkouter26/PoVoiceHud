# Performance evidence

Machine: Intel Core Ultra 9 275HX (8P+16E, 24 threads), RTX 5070 Ti Laptop (unused), Windows 11 26H2, .NET 10.0.400. All measurements 2026-10-01, Debug build, CPU runtimes only.

## SPEC criterion 3 — ASR ≤ 450 ms median

Fixture `tests/PoVoiceHud.Core.Tests/Fixtures/tenant-question.wav` (3.87 s, Windows TTS), `WhisperTranscriberTests.Transcribes_the_tenant_question_within_450ms_median`, base.en, 8 threads, `audio_ctx = 512`:

| run | timings (ms) | median |
|---|---|---|
| solo test | 300 ± 20 (5 runs) | **≈300 ms** ✅ |
| full suite, parallel classes (before serialising the suite) | 472–637 | ✗ → suite made sequential |
| full suite, sequential | passes the 450 ms assertion | ✅ |

Bench grid (scratch script, 3 timed runs, median): base.en ctx 1500 → 995 ms @8 thr / 732 @16; ctx 512 → 300 / 235; tiny.en ctx 512 → 167 / 127 (wrong text). Output text identical for base.en at every setting: "Does the inspection table have a tenant ID foreign key?"

## Criterion 4 — end-to-end ≤ 1 200 ms

`HudPipelineTests.Replayed_question_yields_a_schema_card_within_1200ms` replays the fixture plus 1 s of silence through the real VAD → Whisper → router → `SchemaProvider` and asserts the `[SCHEMA] Inspection.TenantId` card arrives within 1 200 ms of `Start()`. Passes in every suite run (the measurement includes VAD over the whole 4.9 s of audio, not just the post-pause part).

Live (Simple mode, claim played through the laptop speakers, heard by the mic array): transcript 440 ms after the audio ended, `[FACT]` card 38 ms later without an LLM; with Llama-3.3-70B on Azure AI Foundry the verdict card arrives 0.8–1.1 s after the transcript.

## Criterion 10 — memory ≤ 450 MB, CPU ≤ 6 % (8-core) while streaming

`PoVoiceHud.App.exe` in Simple mode, mic open, VAD running on every 100 ms frame, nobody speaking; sampled every 5 s for 60 s with `Get-Process`:

| t (s) | working set | CPU (of 24 threads) |
|---|---|---|
| 5 | 378 MB | 0.05 % |
| 30 | 383 MB | 0.00 % |
| 60 | 387 MB | 0.01 % |

Peak working set **387 MB** ✅ (Whisper base.en + Silero + WinUI). Average CPU **0.03 %** of the machine ≈ **0.1 % 8-core-equivalent** ✅. Transcription is a burst: ~300 ms at 8 threads per utterance.

## Criterion 1 — tests and coverage

`dotnet test --solution PoVoiceHud.sln`: 165 tests, 0 failures, ≈ 15 s sequential.

`dotnet test --solution PoVoiceHud.sln -- --coverage --coverage-output-format cobertura` → **PoVoiceHud.Core line 81.9 %, branch 64.4 %** ✅ (target ≥ 80 % line).

Uncovered by design (need a live device or service): `AzdoBuildClient` (0 %, Azure DevOps HTTP client — provider logic is tested through `IBuildClient`), `WasapiDualCapture` (7 %, WASAPI devices — exercised by the live capture check), `ModelStore` download branch (36 %, models already present), DI registration lambdas.
