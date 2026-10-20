# PoVoiceHud

An always-on-top Windows 11 HUD that fact-checks what is said, as it is said. It transcribes your microphone locally with Whisper. Each sentence that states a fact is checked by `gpt-4.1-mini` on your Azure AI Foundry resource, using Wikipedia articles as evidence. The sentence then turns **green** (correct) or **red** (incorrect), with the reason underneath.

Audio never leaves the machine. Only the text of claims goes to your Foundry endpoint, and their key words go to Wikipedia's search (switch that off in Settings).

## Using it

- **First run:** the window opens on Settings, and the microphone stays off until an endpoint and key pass the connection check (see below). The same happens at a later start if the key is refused or the deployment is gone.
- **The window** is an ordinary one: drag it by the headline, resize, snap, minimize, maximize or close it. Closing quits, so nothing listens after the window is gone. It stays on top of other windows so it can sit beside a call.
- **The header** is the title bar: the microphone with a level meter, the latest verdict or status, a spinner while a claim is being checked, and ⚙. Click the microphone, or use *Mute* in the tray menu, to mute. There is no global hotkey, so no shortcut of Teams or another app is taken. Muting drops anything not yet transcribed. The bar along the header's bottom edge is this session's verdicts as shares of green, red and amber; its tooltip gives the counts.
- **Two pages**, switched at the top of the window. Both keep a readable width when the window is wide.
  - **Live:** every sentence as it is heard, one per line, with the clock time. A claim reads *Checking…* while the model works, then is highlighted *Correct* or *Incorrect* with the reason. A specific claim the model cannot settle gets a quiet *Can't tell* note. Questions, requests, opinions, predictions, remarks about your own company or team, and garbled speech are not claims: they stay plain and dimmed. The page follows the newest line unless you scroll up.
  - **Facts:** only the claims found correct or incorrect, newest first. Each is restated to stand on its own ("It was finished in 1889" becomes "The Eiffel Tower was finished in 1889"), with its reason, its source and when it was said. A claim said twice is one row marked ×2, showing when it was said last. **Copy facts** puts the list on the clipboard.
- **When checking fails** (the service is down or slow, or the key stops working), the header says so once, *Fact checks paused: …*, and the lines stay plain. It says *Fact checks are working again* when they are. A sentence that Azure's content filter refuses gets its own quiet *Can't tell*.
- **Sources:** a link to an article the app looked up itself is marked *(looked up)*. A URL the model wrote is marked *(cited by the model)*, because it may not exist.
- **Tray icon:** left click hides or shows the HUD. The menu has *Mute*/*Unmute*, *Open the history folder*, *Copy diagnostics*, *Check for updates* and *Quit*.

## Settings (⚙)

| Setting | |
|---|---|
| **Azure AI Foundry endpoint** | `https://<resource>.services.ai.azure.com`. The resource needs a deployment named `gpt-4.1-mini`. |
| **Key** | Kept in Windows Credential Manager (`PoVoiceHud/FoundryKey`), never in a file. |
| **Wikipedia look-ups** | On: up to three article intros go to the model as evidence (2.5 s limit; if slow, the model judges alone). Off: nothing goes to Wikipedia. |
| **Also listen to the call** | Also transcribes the speakers' audio, so the other side of a call is checked too. Lines are then labelled *You* and *Them*. |
| **Start with Windows** | |
| **Sounds and animations** | A short cue per verdict and for mute (each under 240 ms, so call capture never transcribes one), and the highlight fading in. Windows' *Animation effects* switch also turns the fade off. |

Settings is a page in the window, so switching to the Azure portal to copy a key leaves it as it is. **Apply** saves and uses the changes without a restart. When the endpoint or key changed, it first sends one message to the model; if that fails, it stays on the page and says why (key refused, no such deployment, unreachable). **Cancel** (or Esc, or ⚙ again) puts back what was applied. A new endpoint, key or Wikipedia setting only replaces the fact checker; *Also listen to the call* restarts the audio and Whisper. `%LOCALAPPDATA%\PoVoiceHud\settings.json` holds the above plus three keys for the file only: `WhisperThreads`, `Vocabulary` (comma-separated names Whisper should expect) and `UpdateFeed`.

## Files

| | `%LOCALAPPDATA%\PoVoiceHud\` |
|---|---|
| Settings | `settings.json` |
| History | `sessions\{date}.jsonl`: every line heard and every verdict, on this PC only |
| Logs | `logs\{date}.log` (14 days) and `crash.log`. *Copy diagnostics* zips these with `settings.json` and a machine description, but never the history or the key. |
| Models | `models\`: `ggml-base.en.bin` (~148 MB) and `silero_vad.onnx`, downloaded on first run and checked against a pinned SHA-256 |

## Build, run, test

```powershell
dotnet build PoVoiceHud.sln
dotnet run --project src/PoVoiceHud.App                         # the real app: opens the microphone
dotnet run --project src/PoVoiceHud.App -- --demo               # a sample conversation, no audio (Debug builds)
dotnet run --project src/PoVoiceHud.App -- --demo --facts       # the same, on the Facts page
dotnet run --project src/PoVoiceHud.App -- --demo --settings    # the Settings page
dotnet run --project src/PoVoiceHud.App -- --demo --setup       # the first-run page
dotnet test --solution PoVoiceHud.sln                           # 80 tests: 66 unit + 14 integration (models, Credential Manager)
pwsh build/pack.ps1                                             # Setup.exe + update packages in artifacts/releases (Velopack)
```

Requirements: Windows 11, .NET 10 SDK, a microphone. No GPU is needed. Build output goes to `artifacts/`.

**Install and update:** Setup.exe installs per user (no admin) to `%LOCALAPPDATA%\PoVoiceHud.App`. It is not code-signed, so SmartScreen warns the first time (**More info → Run anyway**). Raise `<Version>` in `src/PoVoiceHud.App/PoVoiceHud.App.csproj` before packing, and keep `artifacts/releases` between versions for delta updates. With `UpdateFeed` set to an https URL or a folder (publish with `pwsh build/pack.ps1 -Feed <folder>`), an installed copy downloads a newer version in the background and installs it when it closes.

## How it works

`src/PoVoiceHud.Core` has everything testable and never references WinUI. `src/PoVoiceHud.App` is the WinUI 3 window, tray icon and Velopack entry point. `tests/PoVoiceHud.Core.Tests` is xUnit v3.

1. `WasapiDualCapture`: 16 kHz mono frames from the mic (and the speakers' loopback when *Also listen to the call* is on).
2. `SileroVad` + `UtteranceSegmenter`: one utterance per sentence. Anything under 300 ms is dropped, which is why the cues stay shorter.
3. `WhisperTranscriber`: `base.en` on the CPU. `HudPipeline.Sentences` splits what it heard into sentences (not at initials or titles), and each one is a line on the Live page, stamped with the clock time it started.
4. `FactChecker.IsCandidate`: a generous word-list check. It sends on every sentence that has at least three words (numbers count), is not only fillers, and is not a plain question. "…, right?" after a statement still goes; the model's `not_a_claim` settles requests and chatter.
5. `FactChecker`: Wikipedia evidence, then `FoundryClient` (`gpt-4.1-mini`, verdict JSON schema, 8 s per attempt, retried on 429/5xx). The sentence travels as JSON data with today's date and the two sentences heard before it as context, so speech cannot pass as instructions. A sentence that says "it" or "they" is looked up together with the one before it. The model restates the claim to stand on its own. The prompt counts opinions, predictions, anecdotes, remarks about the speaker's own company or team, vague remarks and garbled speech as `not_a_claim`, and those lines stay plain. Verdicts are cached per claim, and per context when the claim leans on it.
6. `HudViewModel`: the line takes its verdict, and correct and incorrect claims go to Facts.

Transcription and checking are separate stages, and up to three claims are checked at once, so a slow model never delays the next line or the next claim. Muting bumps a generation counter, and anything still in flight is discarded. A checker that cannot be reached throws `FactCheckUnavailableException`, which the pipeline reports once as a status.

**Design notes**

- **Whisper on the CPU** with a 10 s audio context: `base.en` takes about 300 ms per sentence on 8 threads, with no GPU driver dependency. `tiny.en` was faster but wrong. A vocabulary prompt adds about 80–100 ms, so it is kept to 120 characters.
- **A fixed cloud model:** `gpt-4.1-mini` answers in about 1–1.5 s and honours the JSON schema. `Llama-3.3-70B-Instruct` was as fast on average but now and then ran past the 8 s limit.
- **Wikipedia as evidence:** a model judging alone invents sources. With the article intros it cites a page the app fetched itself, which the Facts page can mark *(looked up)*.
- **A deterministic pre-filter, not an LLM router:** it costs nothing and is easy to test. It drops only what cannot be a claim: a missed claim costs more than a model call, and `not_a_claim` catches the rest.
- **The present needs a source:** `gpt-4.1-mini`'s knowledge ends in mid-2024. Even when told the date, it judged "the current president is…" from its training. So a claim about now ("current", "still", "latest"…) keeps *Correct* or *Incorrect* only when it rests on an article looked up for it; otherwise it is *Can't tell*.
- **Settings check before they apply:** an endpoint or key is saved only once the model has answered with it, and the app does not listen until one has. It never runs in a state where every claim fails.

Working rules for coding agents are in `AGENTS.md`; `docs/manual-test.md` is the checklist for what the tests cannot reach.
