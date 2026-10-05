namespace PoVoiceHud.Core.Tests.Fixtures;

/// <summary>
/// Tests that load the Whisper and VAD models and assert on timings (Whisper ≤ 450 ms, pipeline ≤ 1 200 ms).
/// Those numbers only mean something when nothing else competes for the CPU, so this collection runs alone;
/// every other test class runs in parallel.
/// </summary>
[CollectionDefinition("Timing", DisableParallelization = true)]
public sealed class TimingGroup;
