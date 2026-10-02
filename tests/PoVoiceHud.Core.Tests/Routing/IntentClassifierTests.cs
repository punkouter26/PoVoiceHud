using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Tests.Routing;

public class IntentClassifierTests
{
    [Theory]
    [InlineData("who modified the invoice service last week", Intent.Git)]
    [InlineData("when was the retry logic added", Intent.Git)]
    [InlineData("can you check the blame on that file", Intent.Git)]
    [InlineData("what was the last commit on the payment controller", Intent.Git)]
    [InlineData("does the inspection table have a tenant id foreign key", Intent.Schema)]
    [InlineData("is tenant id nullable on inspection", Intent.Schema)]
    [InlineData("which columns are indexed on orders", Intent.Schema)]
    [InlineData("did the migration add the status column", Intent.Schema)]
    [InlineData("why did we use a claims header for auth", Intent.Docs)]
    [InlineData("is there an ADR for the caching strategy", Intent.Docs)]
    [InlineData("what's our standard for error handling", Intent.Docs)]
    [InlineData("what pattern does the architecture use for messaging", Intent.Docs)]
    [InlineData("did CI pass on the feature branch", Intent.Pipeline)]
    [InlineData("what's the build status for main", Intent.Pipeline)]
    [InlineData("is the release deployed to staging", Intent.Pipeline)]
    [InlineData("is the pipeline still running", Intent.Pipeline)]
    [InlineData("who changed the schema for the inspection table", Intent.Git | Intent.Schema)]
    [InlineData("did the build break after the migration commit", Intent.Pipeline | Intent.Schema | Intent.Git)]
    [InlineData("what does IAsyncDisposable do again", Intent.None)]
    [InlineData("how does string interpolation work in C sharp", Intent.None)]
    public void Classifies_spoken_questions(string text, Intent expected)
    {
        Assert.Equal(expected, IntentClassifier.Classify(text));
    }

    [Theory]
    [InlineData("what does IAsyncDisposable do again", true)]
    [InlineData("is the build green?", true)]
    [InlineData("Does the inspection table have a tenant ID foreign key?", true)]
    [InlineData("we should refactor that later", false)]
    [InlineData("let me share my screen", false)]
    public void Detects_questions(string text, bool expected)
    {
        Assert.Equal(expected, IntentClassifier.IsQuestion(text));
    }
}
