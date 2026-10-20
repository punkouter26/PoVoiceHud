# ADR-0004: Fact checks look claims up on Wikipedia before the model judges them

Status: Accepted (partly reverses the 2026-10-01 amendment to ADR-0003)
Deciders: punkouter26
Date: 2026-10-19

## Context

Since web evidence was removed (ADR-0003, amendment) the model judges every claim from its own memory, and the link on a verdict is a URL the model wrote. Models invent URLs, so the card has to label the link "cited by the model". The owner chose to bring back a lookup, on two conditions from the original decision: no paid search API and no key to manage.

## Decision

`WikipediaEvidence` (`src/PoVoiceHud.Core/Providers/FactCheck`) calls English Wikipedia's public search API (`/w/api.php`, `generator=search` with `prop=extracts`). There is no key or account. It returns the opening paragraphs (up to 1 200 characters) of the three best-matching articles.

- **What is searched:** the claim's content words (stop words removed). The search requires every word to match, so if that finds nothing it tries again with only the names and numbers. "Python was created by Dennis Ritchie" becomes "Python created Dennis Ritchie", then "Python Dennis Ritchie".
- **Where it sits:** after the local Ollama chatter gate and before the main model. Chatter is never looked up. The passages go into the user message's JSON as `evidence`, and the system prompt tells the model to judge from them first and give the URL it relied on as `source`.
- **Checked links:** `Insight.LinkChecked` is true only when the model's `source` is one of the fetched articles. The card then says `en.wikipedia.org ↗ (looked up)` instead of `(cited by the model)`.
- **Best effort:** the lookup has a 2.5 s limit and is not retried. If it fails or finds nothing, the verdict goes ahead from the model's own knowledge, exactly as before.
- **Switch:** `Settings.LookUpClaims` (on by default), shown in the settings panel as *Look claims up on Wikipedia* with a line that says what leaves the PC.

## Consequences

- One more outbound destination: the content words of each claim go to `en.wikipedia.org`. Audio never does, and neither does the full transcript. Users of a local Ollama model who want nothing to leave the PC turn the switch off.
- About 200–500 ms more per checked claim, on top of the model call. Repeated claims are still answered from the provider's cache.
- Encyclopedic claims (history, science, people, places) now come with a real source. Claims about recent events or private matters usually find no useful article, and the model judges them alone as before.
- Only English Wikipedia, which matches the English-only Whisper model (ADR-0001).
