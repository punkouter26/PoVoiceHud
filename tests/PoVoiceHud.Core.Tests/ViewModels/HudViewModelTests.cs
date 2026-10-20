using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.Core.Tests.ViewModels;

public class HudViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 15, 4, 0, TimeSpan.Zero);
    private readonly List<Uri> _opened = [];
    private readonly HudViewModel _vm;

    public HudViewModelTests()
    {
        _vm = new HudViewModel(autoExpand: TimeSpan.FromSeconds(20), repoRoot: @"C:\src\pmis", openUri: u => { _opened.Add(u); return Task.CompletedTask; });
    }

    private static Insight Card(string headline, double confidence = 0.9) =>
        new(Intent.Schema, headline, "answer", [new Jump(@"C:\src\pmis\src\Domain\Entities\Inspection.cs", 28)], confidence);

    [Fact]
    public void Confident_insight_becomes_focus_and_auto_expands_for_the_configured_time()
    {
        _vm.Push([Card("[SCHEMA] Inspection.TenantId")], T0);

        Assert.True(_vm.IsExpanded);
        Assert.Equal("[SCHEMA] Inspection.TenantId", _vm.Headline);
        Assert.Equal("Inspection.TenantId", _vm.Focus?.Title);

        _vm.Tick(T0.AddSeconds(19));
        Assert.True(_vm.IsExpanded);
        _vm.Tick(T0.AddSeconds(21));
        Assert.False(_vm.IsExpanded);
    }

    [Fact]
    public void Something_said_again_is_one_card_with_a_count_and_history_lines_can_be_brought_back()
    {
        static Insight Fact(string claim, Verdict verdict) => new(Intent.FactCheck, $"[FACT] {claim}", "answer", [], Verdict: verdict);
        Assert.False(_vm.HasFocus);

        _vm.Push([Fact("A tomato is blue", Verdict.Contradicted)], T0);
        Assert.Equal("❌ A tomato is blue", _vm.Headline);   // the pill carries the verdict; the card below it only the reason
        _vm.Push([Fact("a tomato is blue", Verdict.Contradicted)], T0);
        Assert.Equal("❌ a tomato is blue ×2", _vm.Headline);
        Assert.Empty(_vm.Shelf);                             // a repeat of the card on show replaces it in place

        _vm.Push([Fact("A tomato is red", Verdict.Supported)], T0);
        _vm.Push([Fact("A tomato is blue", Verdict.Contradicted)], T0);   // a repeat of an earlier card comes back up from the history
        Assert.Equal("❌ A tomato is blue ×3", _vm.Focus?.Summary);
        Assert.Equal("✅ A tomato is red", Assert.Single(_vm.Shelf).Summary);
        Assert.Equal("❌ 3  ✅ 1".Split("  ").Order(), _vm.Tally.Split("  ").Order());   // the tally still counts every time it was said

        _vm.ShowCommand.Execute(_vm.Shelf[0]);
        Assert.Equal("✅ A tomato is red", _vm.Headline);
        Assert.Equal("❌ A tomato is blue ×3", Assert.Single(_vm.Shelf).Summary);
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
    public void Low_confidence_insight_updates_the_pill_without_expanding()
    {
        _vm.Push([Card("[AZDO] Pipelines", 0.3)], T0);

        Assert.False(_vm.IsExpanded);
        Assert.Equal("[AZDO] Pipelines", _vm.Headline);
    }

    [Fact]
    public void Shelf_keeps_the_six_most_recent_cards_newest_first()
    {
        for (var i = 1; i <= 8; i++) _vm.Push([Card($"[GIT] file{i}")], T0.AddSeconds(i));

        Assert.Equal("file8", _vm.Focus?.Title);
        Assert.Equal(6, _vm.Shelf.Count);
        Assert.Equal(["file7", "file6", "file5", "file4", "file3", "file2"], _vm.Shelf.Select(c => c.Title));
    }

    [Fact]
    public void Secondary_insights_from_the_same_utterance_go_to_the_shelf()
    {
        _vm.Push([Card("[SCHEMA] Inspection"), Card("[GIT] Inspection.cs", 0.8)], T0);

        Assert.Equal("Inspection", _vm.Focus?.Title);
        Assert.Equal("Inspection.cs", Assert.Single(_vm.Shelf).Title);
    }

    [Fact]
    public void Transcript_keeps_the_last_eight_lines_in_order()
    {
        for (var i = 1; i <= 10; i++) _vm.AddTranscript(new TranscriptLine(SourceHint.Mic, TimeSpan.FromSeconds(i), $"line {i}"));

        Assert.Equal(8, _vm.Transcript.Count);
        Assert.Equal("line 3", _vm.Transcript[0].Text);
        Assert.Equal("line 10", _vm.Transcript[^1].Text);
    }

    [Fact]
    public void Pinning_keeps_the_panel_open_until_unpinned()
    {
        _vm.ToggleExpandCommand.Execute(null);
        Assert.True(_vm.IsExpanded);

        _vm.Tick(T0.AddMinutes(5));
        Assert.True(_vm.IsExpanded);

        _vm.ToggleExpandCommand.Execute(null);
        Assert.False(_vm.IsExpanded);
    }

    [Fact]
    public void Pointer_over_the_panel_postpones_collapse()
    {
        _vm.Push([Card("[DOCS] ADR-0004")], T0);
        _vm.IsPointerOver = true;

        _vm.Tick(T0.AddSeconds(30));
        Assert.True(_vm.IsExpanded);

        _vm.IsPointerOver = false;
        _vm.Tick(T0.AddSeconds(31));
        Assert.False(_vm.IsExpanded);
    }

    [Fact]
    public async Task Cards_split_tag_from_title_and_label_jumps_relative_to_the_repo()
    {
        _vm.Push([Card("[SCHEMA] Inspection.TenantId")], T0);
        var card = _vm.Focus!;

        Assert.Equal("SCHEMA", card.Tag);
        Assert.Equal("Inspection.TenantId", card.Title);
        var jump = Assert.Single(card.Jumps);
        Assert.Equal("src/Domain/Entities/Inspection.cs:28", jump.Label);

        await jump.OpenCommand.ExecuteAsync(null);
        Assert.Equal("vscode://file/C:/src/pmis/src/Domain/Entities/Inspection.cs:28", Assert.Single(_opened).OriginalString);
    }

    [Fact]
    public void Mute_toggle_flips_the_flag_and_the_pill_shows_the_latest_status()
    {
        _vm.Push([Card("[GIT] file1")], T0);
        _vm.SetStatus("Listening");
        Assert.Equal("Listening", _vm.Headline);   // there is no status row: the pill shows whichever came last

        _vm.ToggleMuteCommand.Execute(null);
        Assert.True(_vm.IsMuted);
        Assert.Equal("Muted", _vm.StatusText);
        Assert.Equal("Muted", _vm.Headline);

        _vm.ToggleMuteCommand.Execute(null);
        Assert.Equal("Listening", _vm.Headline);
    }

    [Fact]
    public void Verdicts_are_tallied_for_the_session_and_split_by_who_spoke()
    {
        static Insight Fact(Verdict verdict) => new(Intent.FactCheck, "[FACT] x", "answer", [], Verdict: verdict);
        Assert.Equal("", _vm.Tally);

        _vm.Push([Fact(Verdict.Supported)], T0);
        _vm.Push([Fact(Verdict.Contradicted)], T0, SourceHint.Teams);
        _vm.Push([Fact(Verdict.Supported)], T0, SourceHint.Teams);
        _vm.Push([Card("[GIT] file1")], T0);   // not a verdict: leaves the tally alone

        Assert.Equal("✅ 2  ❌ 1", _vm.Tally);
        Assert.Equal("This session — you: ✅ 1 · the call: ✅ 1  ❌ 1", _vm.TallyDetail);
        Assert.Equal(Verdict.Supported, _vm.Shelf[0].Verdict);
    }

    [Fact]
    public void Cards_can_be_rated_and_put_away_say_who_spoke_on_a_call_and_muting_suggests_a_recap()
    {
        var rated = new List<(string, Rating)>();
        _vm.Rated += c => rated.Add((c.Title, c.Rating));
        _vm.Apply(new Settings { AudioInput = AudioInput.Both });   // call audio heard: cards say who spoke
        _vm.Push([Card("[GIT] file1")], T0, SourceHint.Teams);
        _vm.Push([Card("[GIT] file2")], T0);

        var focus = _vm.Focus!;
        Assert.True(focus.ShowSpeaker);
        Assert.Equal("You", focus.Speaker);
        Assert.Equal("Them", _vm.Shelf[0].Speaker);

        focus.RateUpCommand.Execute(null);
        focus.RateDownCommand.Execute(null);
        focus.RateDownCommand.Execute(null);   // pressing it again takes the rating back
        Assert.Equal([("file2", Rating.Up), ("file2", Rating.Down), ("file2", Rating.None)], rated);
        _vm.SetRatings(9, 1);
        Assert.Equal("Your ratings, last 7 days: 👍 9  👎 1 (90% right)", _vm.TallyDetail);

        focus.DismissCommand.Execute(null);
        Assert.Equal("file1", _vm.Focus?.Title);   // the newest history card takes its place
        Assert.Empty(_vm.Shelf);

        _vm.RecapHotkey = "Ctrl+Alt+S";
        for (var i = 0; i < 6; i++) _vm.AddTranscript(new TranscriptLine(SourceHint.Mic, TimeSpan.FromSeconds(i), $"line {i}"));
        _vm.ToggleMuteCommand.Execute(null);
        Assert.Equal("Muted · recap this call? Ctrl+Alt+S", _vm.Headline);
        _vm.RecapDone();
        Assert.Equal("Muted", _vm.Headline);
    }

    [Fact]
    public void Collapse_closes_the_panel_and_unpins_it_and_the_transcript_folds_to_its_last_line()
    {
        _vm.ToggleExpandCommand.Execute(null);
        _vm.CollapseCommand.Execute(null);
        Assert.False(_vm.IsExpanded);
        Assert.False(_vm.IsPinned);

        _vm.AddTranscript(new TranscriptLine(SourceHint.Mic, TimeSpan.FromSeconds(1), "line 1"));
        Assert.Equal("line 1", _vm.LastLine?.Text);
        Assert.True(_vm.IsTranscriptClosed);
        _vm.ToggleTranscriptCommand.Execute(null);
        Assert.True(_vm.IsTranscriptOpen);
    }
}
