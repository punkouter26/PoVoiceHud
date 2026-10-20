using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.Core.Tests.ViewModels;

public class HudViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 15, 4, 0, TimeSpan.Zero);
    private readonly List<Uri> _opened = [];
    private readonly HudViewModel _vm;

    public HudViewModelTests()
    {
        _vm = new HudViewModel(openUri: u => { _opened.Add(u); return Task.CompletedTask; });
    }

    private static TranscriptLine Line(string text, int second = 1, SourceHint source = SourceHint.Mic) => new(source, TimeSpan.FromSeconds(second), text);

    private static Insight Judged(string claim, Verdict verdict, string reason = "Because.") => new(claim, verdict, reason, 0.9);

    [Fact]
    public void A_line_shows_checking_while_it_is_with_the_checker_then_its_verdict_in_plain_words()
    {
        var line = Line("Python was created by Dennis Ritchie.");
        _vm.AddTranscript(line);
        var live = Assert.Single(_vm.Live);
        Assert.Equal(LineState.Heard, live.State);
        Assert.False(live.HasNote);

        _vm.MarkChecking(line, busy: true);
        Assert.Equal("Checking…", live.Note);
        Assert.True(_vm.IsChecking);

        _vm.Push(new Checked(line, Judged("Python was created by Dennis Ritchie", Verdict.Contradicted, "Guido van Rossum created Python.")), T0);
        _vm.MarkChecking(line, busy: false);
        Assert.Equal(LineState.Incorrect, live.State);
        Assert.Equal("Incorrect — Guido van Rossum created Python.", live.Note);
        Assert.False(_vm.IsChecking);
        Assert.Equal("❌ Python was created by Dennis Ritchie", _vm.Headline);   // the pill shows the latest verdict

        var chatter = Line("I think we should keep going with this.", 5);
        _vm.AddTranscript(chatter);
        _vm.MarkChecking(chatter, busy: true);
        _vm.MarkChecking(chatter, busy: false);   // the checker said it is not a claim: no mark at all
        Assert.Equal(LineState.Heard, _vm.Live[^1].State);

        var vague = Line("Octopuses dream in colour.", 9);
        _vm.AddTranscript(vague);
        _vm.Push(new Checked(vague, Judged("Octopuses dream in colour", Verdict.Unverified, "Unknown.")), T0);
        Assert.Equal("Can't tell — Unknown.", _vm.Live[^1].Note);
        Assert.Equal("❌ Python was created by Dennis Ritchie", _vm.Headline);   // "can't tell" does not take over the headline
    }

    [Fact]
    public void Facts_keep_only_correct_and_incorrect_claims_newest_first_with_repeats_counted()
    {
        void Say(string claim, Verdict verdict, int second)
        {
            var line = Line(claim, second);
            _vm.AddTranscript(line);
            _vm.Push(new Checked(line, Judged(claim, verdict)), T0.AddSeconds(second));
        }

        Say("The Eiffel Tower was completed in 1889", Verdict.Supported, 1);
        Say("Octopuses dream in colour", Verdict.Unverified, 2);
        Say("Python was created by Dennis Ritchie", Verdict.Contradicted, 3);
        Say("the eiffel tower was completed in 1889", Verdict.Supported, 4);

        Assert.Equal(["The Eiffel Tower was completed in 1889 ×2", "Python was created by Dennis Ritchie"], _vm.Facts.Select(f => f.Title));
        Assert.Equal("1 correct · 1 incorrect", _vm.FactsSummary);
        Assert.Equal(LineState.CantTell, _vm.Live[1].State);   // "can't tell" shows on the Live page only
        Assert.Equal((2, 1, 1), (_vm.Supported, _vm.Contradicted, _vm.Unverified));
        Assert.Equal("15:04", _vm.Facts[0].Time);
    }

    [Fact]
    public async Task Copy_facts_writes_one_line_per_fact_and_a_fact_links_its_source()
    {
        var wiki = new Uri("https://en.wikipedia.org/wiki/Eiffel_Tower");
        var line = Line("The Eiffel Tower was completed in 1889.");
        _vm.AddTranscript(line);
        _vm.Push(new Checked(line, new Insight("The Eiffel Tower was completed in 1889", Verdict.Supported, "Completed in March 1889.", 0.9, wiki, LinkChecked: true)), T0);

        var fact = Assert.Single(_vm.Facts);
        Assert.Equal("en.wikipedia.org ↗ (looked up)", fact.LinkLabel);
        Assert.Equal("✅ Correct: The Eiffel Tower was completed in 1889 — Completed in March 1889. (https://en.wikipedia.org/wiki/Eiffel_Tower)", _vm.FactsText);

        await fact.OpenLinkCommand.ExecuteAsync(null);
        Assert.Equal(wiki, Assert.Single(_opened));
    }

    [Fact]
    public void The_live_page_shows_first_and_keeps_the_last_lines()
    {
        Assert.True(_vm.IsLiveOpen);
        _vm.IsFactsOpen = true;
        Assert.False(_vm.IsLiveOpen);

        for (var i = 1; i <= HudViewModel.LiveSize + 5; i++) _vm.AddTranscript(Line($"line {i}", i));
        Assert.Equal(HudViewModel.LiveSize, _vm.Live.Count);
        Assert.Equal("line 6", _vm.Live[0].Text);
    }

    [Fact]
    public void Lines_say_who_spoke_only_when_the_call_is_heard_too_and_a_new_pipeline_clears_checking()
    {
        _vm.AddTranscript(Line("first line here", 1));
        Assert.Equal("", _vm.Live[0].Speaker);

        _vm.Apply(new Settings { ListenToCall = true });
        var theirs = Line("second line here", 62, SourceHint.Teams);
        _vm.AddTranscript(theirs);
        Assert.Equal("Them:", _vm.Live[1].Speaker);
        Assert.Equal("01:02", _vm.Live[1].Stamp);

        _vm.MarkChecking(theirs, busy: true);
        _vm.ResetChecking();
        Assert.Equal(LineState.Heard, _vm.Live[1].State);
        Assert.False(_vm.IsChecking);
    }

    [Theory]
    [InlineData("Close your border. It'll be the end! Was it?", new[] { "Close your border.", "It'll be the end!", "Was it?" })]
    [InlineData("The U.S. President met Dr. Smith in 2019. Then he left.", new[] { "The U.S. President met Dr. Smith in 2019.", "Then he left." })]
    [InlineData("  we ask for…  ", new[] { "we ask for…" })]
    public void A_transcription_is_split_into_sentences_but_not_at_initials_or_titles(string text, string[] expected)
    {
        Assert.Equal(expected, HudPipeline.Sentences(text));
    }

    [Theory]
    [InlineData(0, 0.0)]          // silence
    [InlineData(1036, 0.5)]       // -30 dBFS ≈ normal speech → mid-scale
    public void Mic_level_maps_rms_to_a_0_to_1_db_scale(short amplitude, double expected)
    {
        var frame = new short[160];
        Array.Fill(frame, amplitude);
        Assert.Equal(expected, HudPipeline.LevelOf(frame), 2);
    }

    [Fact]
    public void Mute_toggle_flips_the_flag_and_the_pill_shows_the_latest_status()
    {
        _vm.SetStatus("Listening");
        Assert.Equal("Listening", _vm.Headline);

        _vm.ToggleMuteCommand.Execute(null);
        Assert.True(_vm.IsMuted);
        Assert.Equal("Muted", _vm.Headline);

        _vm.ToggleMuteCommand.Execute(null);
        Assert.Equal("Listening", _vm.Headline);
    }
}
