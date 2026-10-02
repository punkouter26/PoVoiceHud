# ADR-0003: Fact-check verdicts from an OpenAI-compatible endpoint (Azure AI Foundry or Ollama)

Status: Accepted
Deciders: punkouter26
Date: 2026-10-01

## Context

Mid-build the owner asked for a simple mode: speak into the laptop mic and have statements fact-checked against the web and an LLM while speaking, with repo sources as optional toggles. They wanted "Llama or a cheap LLM in Azure AI Foundry", and later asked about local Ollama.

## Decision

`FactCheckProvider` (`src/PoVoiceHud.Core/Providers/FactCheck`) fetches up to five Brave results as evidence and asks one chat model for a JSON verdict. The model is reached through plain `HttpClient` with the OpenAI chat-completions body:

- Azure AI Foundry resource root → `POST {root}/models/chat/completions?api-version=2024-05-01-preview` with the `api-key` header (serverless catalog models: Llama-3.3-70B-Instruct, Phi-4, gpt-4.1-nano, …).
- Any full URL (e.g. Ollama `http://localhost:11434/v1/chat/completions`) is used as-is; `http` is allowed only on localhost.

JSON is requested in the prompt and parsed leniently (first `{…}`), because Llama deployments advertise text output only. Without evidence the model judges from its own knowledge; every failure degrades to an `⚠️ Unverified` card.

## Consequences

- No SDK dependency; swapping models is a settings change.
- Privacy: in Simple mode the spoken statement and snippets leave the machine (Brave; the user's own Azure tenant or localhost). Audio never does.
- Measured: Llama-3.3-70B on Foundry answers in 0.8–1.1 s; verdicts were correct on supported/contradicted/unverifiable samples.
