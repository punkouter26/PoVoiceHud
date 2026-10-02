using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Tests.Routing;

public class NoiseFilterTests
{
    [Theory]
    [InlineData("yeah")]
    [InlineData("okay sounds good")]
    [InlineData("Thanks everyone.")]
    [InlineData("um right okay")]
    [InlineData("can you hear me")]
    [InlineData("Good morning, everyone!")]
    [InlineData("")]
    [InlineData("   ")]
    public void Drops_fillers_pleasantries_and_very_short_lines(string text)
    {
        Assert.True(NoiseFilter.IsNoise(text));
    }

    [Theory]
    [InlineData("does the inspection table have a tenant id foreign key")]
    [InlineData("who modified the invoice service")]
    [InlineData("let me check the exact schema definition")]
    [InlineData("Jesus is alive.")]
    [InlineData("who changed this")]
    public void Keeps_substantive_lines(string text)
    {
        Assert.False(NoiseFilter.IsNoise(text));
    }
}
