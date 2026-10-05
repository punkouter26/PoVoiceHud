using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Tests.Routing;

public class NoiseFilterTests
{
    [Theory]
    [InlineData("okay sounds good", true)]          // fillers only
    [InlineData("Good morning, everyone!", true)]   // pleasantry
    [InlineData("   ", true)]                       // nothing
    [InlineData("who changed this", false)]         // three real words is the minimum kept
    [InlineData("Jesus is alive.", false)]
    public void Drops_fillers_and_very_short_lines_but_keeps_substantive_ones(string text, bool noise)
    {
        Assert.Equal(noise, NoiseFilter.IsNoise(text));
    }
}
