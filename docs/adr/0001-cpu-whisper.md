# ADR-0001: Whisper on CPU with a short audio context

Status: Accepted
Deciders: punkouter26
Date: 2026-10-01

## Context

The spec asked for DirectML-accelerated Whisper. Whisper.net 1.9.x ships CPU, CUDA, Vulkan, OpenVINO, CoreML and Metal runtimes — no DirectML. The target laptop has an RTX GPU, but the owner chose to avoid driver dependencies.

Measured on a Core Ultra 9 275HX with the 3.9 s fixture (`src/PoVoiceHud.Core/Asr`):

| model | audio_ctx | 8 threads | 16 threads | text |
|---|---|---|---|---|
| base.en | 1500 (30 s window) | 995 ms | 732 ms | correct |
| base.en | 512 (10 s window) | 300 ms | 235 ms | correct |
| tiny.en | 512 | 167 ms | 127 ms | "for Enkey" (wrong) |

## Decision

`Whisper.net.Runtime` (CPU) with `base.en`. Two processors per model: `audio_ctx = 512` for utterances ≤ 10 s (the conversational case) and the full window for longer ones. Thread count is a setting (default 8).

## Consequences

- Utterances ≤ 10 s meet the 450 ms ASR budget; 10–30 s utterances take 0.7–1 s.
- No GPU runtime to install; works on any x64 machine with AVX.
- `tiny.en` stays selectable in Settings for slow machines, at a known accuracy cost on identifiers.
