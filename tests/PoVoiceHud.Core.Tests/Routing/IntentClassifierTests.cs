using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Tests.Routing;

public class IntentClassifierTests
{
    // One spoken question per source, one that belongs to several, and one that belongs to none.
    [Theory]
    [InlineData("who modified the invoice service last week", Intent.Git)]
    [InlineData("does the inspection table have a tenant id foreign key", Intent.Schema)]
    [InlineData("why did we use a claims header for auth", Intent.Docs)]
    [InlineData("did CI pass on the feature branch", Intent.Pipeline)]
    [InlineData("did the build break after the migration commit", Intent.Pipeline | Intent.Schema | Intent.Git)]
    [InlineData("what does IAsyncDisposable do again", Intent.None)]
    public void Classifies_spoken_questions(string text, Intent expected)
    {
        Assert.Equal(expected, IntentClassifier.Classify(text));
    }

    [Theory]
    [InlineData("The Eiffel Tower was completed in 1889", false, true)]
    [InlineData("is the build green?", true, false)]                   // question mark
    [InlineData("what does IAsyncDisposable do again", true, false)]   // question word, no punctuation
    [InlineData("please open the invoice service", false, false)]      // a request is neither a question nor a claim
    public void Tells_questions_claims_and_requests_apart(string text, bool question, bool declarative)
    {
        Assert.Equal(question, IntentClassifier.IsQuestion(text));
        Assert.Equal(declarative, IntentClassifier.IsDeclarative(text));
    }
}
