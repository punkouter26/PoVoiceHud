using Xunit.Sdk;
using Xunit.v3;

// Timing assertions (Whisper ≤ 450 ms, pipeline ≤ 1 200 ms) are only meaningful when nothing else competes for
// the CPU, so the whole assembly runs sequentially.
[assembly: Parallelization(Mode = ParallelMode.None)]
