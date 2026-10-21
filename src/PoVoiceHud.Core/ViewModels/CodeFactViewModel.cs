using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Repos;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>One file a code fact rests on: its name and folder, and two ways to open it.</summary>
public sealed class CodeSourceViewModel(CodeSource source, Func<Uri, Task> openUri)
{
    public string Path => source.Path;
    /// <summary>"InvoiceService.cs ↗" and, dimmer, where it sits: "src/Services".</summary>
    public string Label => $"{System.IO.Path.GetFileName(source.Path)} ↗";
    public string Folder => System.IO.Path.GetDirectoryName(source.Path)?.Replace('\\', '/') ?? "";
    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => openUri(source.Link));
    public IAsyncRelayCommand OpenInEditorCommand { get; } = new AsyncRelayCommand(() => openUri(source.Editor));
}

/// <summary>
/// One row of the Code facts page: a question about the code and its answer, or a statement about it and its verdict,
/// with the files it rests on.
/// </summary>
public sealed class CodeFactViewModel(CodeFact fact, DateTimeOffset at, SourceHint source, Func<Uri, Task> openUri)
{
    public CodeFact Fact => fact;
    public bool IsAnswer => fact.IsAnswer;
    public Verdict? Verdict => fact.Verdict;
    /// <summary>"Answer", or the verdict in plain words.</summary>
    public string Label => fact.Verdict is { } verdict ? verdict.Label() : "Answer";
    public string Topic => fact.Topic;
    public string Text => fact.Text;
    public string Time { get; } = at.ToString("t", CultureInfo.CurrentCulture);
    public string Speaker { get; } = source.Speaker();
    public IReadOnlyList<CodeSourceViewModel> Sources { get; } = [.. fact.Sources.Select(s => new CodeSourceViewModel(s, openUri))];
    public bool HasSources => fact.Sources.Count > 0;

    /// <summary>One line for "Copy": "💬 Answer: question — answer (files)" or "❌ Incorrect: statement — reason (files)".</summary>
    public string CopyLine =>
        $"{(fact.Verdict is { } verdict ? verdict.Emoji() : "💬")} {Label}: {Topic} — {Text}{(fact.Sources.Count > 0 ? $" ({string.Join(", ", fact.Sources.Select(s => s.Path))})" : "")}";
}
