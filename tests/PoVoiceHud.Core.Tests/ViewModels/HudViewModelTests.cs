using PoVoiceHud.Core.Audio;
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
    public void HasFocus_follows_the_focus_card()
    {
        Assert.False(_vm.HasFocus);
        _vm.Push([Card("[FACT] x")], T0);
        Assert.True(_vm.HasFocus);
    }

    [Theory]
    [InlineData(0, 0.0)]          // silence
    [InlineData(32767, 1.0)]      // full scale
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
    public void Mute_toggle_flips_the_flag_and_status_text()
    {
        _vm.SetStatus("Listening");
        _vm.ToggleMuteCommand.Execute(null);

        Assert.True(_vm.IsMuted);
        Assert.Equal("Muted", _vm.StatusText);
    }
}
