# Manual test checklist

Run `dotnet run --project src/PoVoiceHud.App` (add `-- --pin` to start expanded). Tick what you verified.

## Simple mode (default)

- [ ] Pill shows a green microphone and `Listening`; the five bars next to the microphone light up while you speak (none in silence, three or four for normal speech).
- [ ] Say "The Eiffel Tower was completed in 1889." → a thin bar runs along the pill while it is checked, then the pill reads `✅ The Eiffel Tower was completed in 1889` and the card under it holds only the reason, `✅ Supported — …`, and a link labelled with its host (needs the Foundry/Ollama endpoint; without it `⚠️ Unverified (no LLM configured)`).
- [ ] Say "Python was created by Dennis Ritchie." → the pill reads `❌ Python was created by Dennis Ritchie`, the card says `❌ Contradicted — …`, a short tone plays, and the pill's bottom edge turns red.
- [ ] The pill counts the session: `✅ 1  ❌ 1`; its tooltip splits the count into you and the call.
- [ ] Say the first sentence again → no second card: the same one comes back at once with `×2`. Ctrl+Alt+R → it is checked again.
- [ ] Earlier cards are one line each under the main card; clicking a line makes it the main card and the old main card takes its place in the list.
- [ ] Ask a question ("is the build green?") → no card (questions are not fact-checked in Simple mode).
- [ ] Mute hotkey (hover the microphone to see which one is live) → microphone turns red with a slash, pill says `Muted`; speak → nothing appears; press again → listening resumes.
- [ ] The copy button on a card puts its headline, answer and link on the clipboard.
- [ ] Drag the window near a screen edge → it snaps.
- [ ] The card auto-collapses after 20 s unless the pointer is over it; clicking the pill (there is no chevron button) or Ctrl+E pins it open; Esc collapses it.
- [ ] The window is only as tall as what it shows (no empty space under the cards).

## Settings (⚙)

- [ ] With a working model the settings show two things: the model as one line with **Change…**, and Repository (optional).
- [ ] **Change…** unfolds the model fields. *Local Ollama* hides Endpoint and API key and fills in a model Ollama has; *Azure AI Foundry* brings the saved endpoint back.
- [ ] **Test** with a good endpoint/key → `✓ <model> answered.`; with a wrong key → `✗ … HTTP 401`.
- [ ] There is no Save button: change something and close the settings → the pill goes through `Loading models…` back to `Listening`. Close them without changing anything → nothing reloads.
- [ ] Type an endpoint that is not a URL and close the settings → the pill says `Settings not applied — …`; reopening shows the field and the error.
- [ ] There is no Mode choice: typing or browsing a repository shows the access token and **Test repository**, and closing the settings switches to Repo mode; clearing it goes back to Simple.
- [ ] Repository = a folder with a git repo → Test repository says `✓ Git repository on branch …`; a folder without `.git` → `✗ That folder is not a git repository`.
- [ ] Repository = an Azure DevOps URL + token → Test repository says `✓ Connected: N branches found.`; a wrong token → `✗ Could not connect …`. *The URL path has only been tried against a public GitHub repo, never a real Azure DevOps one.*
- [ ] Close the settings with a URL → the pill says `Fetching the repository…`, then `Listening to you and the call`; the clone is under `%LOCALAPPDATA%\PoVoiceHud\repos`.
- [ ] Browse… to a folder without `.git` → red "not a git repository"; a real repo is accepted.
- [ ] A repository that is neither a git folder nor an `https://…` URL is refused when the settings close (the pill says so).
- [ ] Open settings while the panel is collapsed, then close them → the panel collapses again (settings do not leave it pinned).

## Repo mode (live Teams call, with a repo selected)

- [ ] A plain fact ("The Eiffel Tower was completed in 1889.") still gets its `[FACT]` card: Repo mode does everything Simple does.
- [ ] A sentence naming something in the repo ("the invoice service should retry failed payments") → a `[CODE]` card saying what that code does, with a link to the file, and no `[FACT]` card for it.
- [ ] Schema question ("does the Inspection table have a TenantId foreign key?") → `[SCHEMA]` card with the exact answer and two file links; a link opens VS Code at the line.
- [ ] Git question ("who modified the invoice service?") → `[GIT]` card with author, relative time, subject and SHA.
- [ ] ADR question ("why did we use a claims header for auth?") → `[DOCS]` card; a `⚠️ STALE` line shows when the documented code changed > 14 days after the doc.
- [ ] CI question ("did CI pass on this branch?") → `[AZDO]` card for the current branch (needs org URL, project and PAT). *Never verified against a live organization — and the client was rewritten from the SDK to plain REST on 2026-10-05, so this is the first thing to check.*
- [ ] The transcript line (always shown, in both modes) holds the last sentence; clicking it shows the recent ones.
- [ ] Unplug/replug the headset → the pill briefly says `Audio device changed`, listening resumes.
