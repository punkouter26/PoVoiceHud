# ADR-0002: Deterministic intent router

Status: Accepted
Deciders: punkouter26
Date: 2026-10-01

## Context

The original architecture sketch listed a local LLM (Ollama/Phi) as the router. The end-to-end budget is 1 200 ms from speech pause to card, and base.en already spends ~300 ms of it.

## Decision

Routing is rule-based (`src/PoVoiceHud.Core/Routing`): a filler/word-count noise filter, a keyword table per intent (`IntentClassifier`), a fuzzy symbol matcher over the repo's files, EF entities and ADR titles (`SymbolMatcher`, FuzzySharp token ratio ≥ 80), and a per-source enable mask from Settings. Declarative statements go to the fact checker when that source is on; symbol-less questions go to the web provider when it is on.

## Consequences

- Routing costs < 10 ms and is fully unit-tested with labelled utterances.
- Vocabulary is explicit: new question styles need a keyword, not a prompt change.
- The only LLM call in the product is the fact-check verdict (ADR-0003).
