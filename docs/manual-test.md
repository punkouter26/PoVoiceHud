# Manual test

What the unit and integration tests cannot reach: a live microphone, a real Foundry resource, the window and the tray.

## First run

- [ ] With no `settings.json`, the window opens on the Live page, which says *Listening…*. A claim gets *Can't tell — add the endpoint and key in Settings*.
- [ ] ⚙ → endpoint `https://<resource>.services.ai.azure.com` and the key → **Check** says `✓ Connected — gpt-4.1-mini answered.` with a green dot. A wrong key says `The key was refused (HTTP 401)` with a red dot.
- [ ] Close the settings → the header goes through *Loading models…* back to *Listening*. Close them again without changing anything → nothing reloads.
- [ ] An endpoint that is not https → the header says *Settings not applied — …*.

## Live and Facts

- [ ] "The Eiffel Tower was completed in 1889." → the line reads *Checking…*, then turns green with *Correct — …* within about 2 s, a rising two-note cue plays, and Facts has it with `en.wikipedia.org ↗ (looked up)`.
- [ ] "Python was created by Dennis Ritchie." → red, *Incorrect — …*, a low buzz, on Facts.
- [ ] "Octopuses dream in colour." → a quiet *Can't tell* note with no highlight, on the Live page only; the headline keeps the last real verdict.
- [ ] A run-on stretch ("Close your border. It'll be the end.") → one line per sentence. An opinion ("They don't love the art.") → a plain, dimmed line.
- [ ] "Can you hear me?" and "Let's move on." → plain lines, no highlight, no sound.
- [ ] Say the Eiffel Tower sentence again → Facts shows it once, marked ×2, at the top.
- [ ] Scroll the Live page up → new lines no longer pull it down. Scroll back to the end → it follows again.
- [ ] **Copy facts** → the button says *Copied*, and pasting gives one `✅ Correct: … — … (link)` line per fact.
- [ ] Minimize, maximize and restore → the pages fill the window and the Live page keeps its place. Close and reopen the app → it comes back at the same size and position.

## Window, audio and tray

- [ ] Mute with the microphone and with Win+Shift+M: a falling chirp, the header says *Muted*, and nothing more is transcribed. A line that was *Checking…* goes plain.
- [ ] *Also listen to the call* on, then play a video with speech → its lines are labelled *Them*.
- [ ] Snap the window to a screen edge with Win+Arrow, and resize it down to its minimum (420 × 320) → nothing is cut off; long pages scroll.
- [ ] *Sounds and animations* off → no cues, and highlights appear without fading in.
- [ ] Tray: left click hides and shows the HUD. *Open the history folder* opens `sessions`. *Copy diagnostics* puts a zip on the clipboard (logs, `settings.json`, `environment.txt`, no `sessions`). *Quit* closes the app and removes the icon. *Quit PoVoiceHud* in Settings does the same.
- [ ] *Start with Windows* adds or removes `PoVoiceHud` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- [ ] *Not yet run on this machine:* `pwsh build/pack.ps1`, install `artifacts\releases\PoVoiceHud.App-win-Setup.exe`, raise `<Version>`, pack again with `-Feed <folder>`, set `UpdateFeed` to that folder and start the installed copy. The header should say the new version downloaded, and closing it should install that version.
