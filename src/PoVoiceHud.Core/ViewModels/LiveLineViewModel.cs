using CommunityToolkit.Mvvm.ComponentModel;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>Where a line of the Live page is: just heard, with the fact checker, or judged.</summary>
public enum LineState { Heard, Checking, Correct, Incorrect, CantTell }

/// <summary>One line of the Live page: what was said, highlighted by its verdict once there is one.</summary>
public sealed partial class LiveLineViewModel(TranscriptLine line, bool showSpeaker) : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Note), nameof(HasNote))] private LineState _state;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Note))] private string _reason = "";

    public TranscriptLine Line => line;
    public string Text => line.Text;
    /// <summary>Minutes:seconds since listening started.</summary>
    public string Stamp => $"{line.At:mm\\:ss}";
    /// <summary>"You:" or "Them:" when the call is heard too; nothing when only the microphone is.</summary>
    public string Speaker => showSpeaker ? $"{line.Source.Speaker()}:" : "";

    /// <summary>The line under the sentence: "Checking…", or the verdict in plain words and its reason.</summary>
    public string Note => State switch
    {
        LineState.Checking => "Checking…",
        LineState.Correct => $"{Verdict.Supported.Label()} — {Reason}",
        LineState.Incorrect => $"{Verdict.Contradicted.Label()} — {Reason}",
        LineState.CantTell => $"{Verdict.Unverified.Label()} — {Reason}",
        _ => "",
    };

    public bool HasNote => State != LineState.Heard;

    internal void Judge(Insight verdict)
    {
        Reason = verdict.Reason;
        State = verdict.Verdict switch
        {
            Verdict.Supported => LineState.Correct,
            Verdict.Contradicted => LineState.Incorrect,
            _ => LineState.CantTell,
        };
    }
}
