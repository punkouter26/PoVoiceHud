# Manual test checklist

Run `dotnet run --project src/PoVoiceHud.App` (add `-- --pin` to start expanded). Tick what you verified.

## Simple mode (default)

- [ ] Pill shows a green dot and `PoVoiceHud — listening`; expanded status bar reads `Mic · Fact check · Listening (mic)`.
- [ ] Say "The Eiffel Tower was completed in 1889." → within ~2 s a `[FACT]` card: `✅ Supported — …` with a source link (needs the Foundry/Ollama endpoint; without it `⚠️ Unverified (no LLM configured)`).
- [ ] Say "Python was created by Dennis Ritchie." → `❌ Contradicted — …`.
- [ ] Ask a question ("is the build green?") → no card (questions are not fact-checked in Simple mode).
- [ ] Win+Shift+M → dot turns red, status `Muted`; speak → nothing appears; Win+Shift+M again → listening resumes.
- [ ] Drag the window near a screen edge → it snaps.
- [ ] The card auto-collapses after 20 s unless the pointer is over it; clicking the pill pins it open.

## Settings (⚙)

- [ ] Mode → Repo flips audio to `Both` and turns the five repo/web sources on; toggles remain individually editable.
- [ ] Browse… to a folder without `.git` → red "not a git repository"; a real repo is accepted.
- [ ] "Use local Ollama (no key)" fills `http://localhost:11434/v1/chat/completions` / `llama3.2`.
- [ ] Save, then "Restart to apply" → app restarts with the new settings.

## Repo mode (live Teams call, with a repo selected)

- [ ] Schema question ("does the Inspection table have a TenantId foreign key?") → `[SCHEMA]` card with the exact answer and two `[Jump]` buttons; Jump opens VS Code at the line.
- [ ] Git question ("who modified the invoice service?") → `[GIT]` card with author, relative time, subject and SHA.
- [ ] ADR question ("why did we use a claims header for auth?") → `[DOCS]` card; `⚠️ STALE` shows when the documented code changed > 14 days after the doc.
- [ ] CI question ("did CI pass on this branch?") → `[AZDO]` card for the current branch (needs org URL, project and PAT). *Not yet verified against a live organization.*
- [ ] Library question ("what does IAsyncDisposable do?") → `[WEB]` card (or `[Search]` link without a Brave key).
- [ ] Unplug/replug the headset → status briefly says `Audio device changed`, listening resumes.
