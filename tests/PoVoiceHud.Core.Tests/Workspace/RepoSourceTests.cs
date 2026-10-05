using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests.Workspace;

[Trait("Category", "Integration")]
public sealed class RepoSourceTests : IDisposable
{
    private readonly string _clone = Path.Combine(Path.GetTempPath(), "PoVoiceHud.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Reads_organization_project_and_repository_from_azure_devops_urls()
    {
        Assert.True(RepoSource.TryParseAzdo("https://dev.azure.com/acme/PMIS%20Core/_git/pmis-api", out var org, out var project, out var repo));
        Assert.Equal(("https://dev.azure.com/acme", "PMIS Core", "pmis-api"), (org.ToString(), project, repo));

        Assert.True(RepoSource.TryParseAzdo("https://acme@dev.azure.com/acme/PMIS/_git/pmis", out org, out project, out _));   // the form "Clone" copies
        Assert.Equal(("https://dev.azure.com/acme", "PMIS"), (org.ToString(), project));

        Assert.True(RepoSource.TryParseAzdo("https://acme.visualstudio.com/DefaultCollection/PMIS/_git/pmis", out org, out project, out _));
        Assert.Equal(("https://acme.visualstudio.com/", "PMIS"), (org.ToString(), project));

        Assert.False(RepoSource.TryParseAzdo("https://github.com/acme/pmis.git", out _, out _, out _));
        Assert.False(RepoSource.TryParseAzdo(null, out _, out _, out _));

        Assert.True(RepoSource.IsRemote("https://dev.azure.com/acme/PMIS/_git/pmis"));
        Assert.False(RepoSource.IsRemote("http://dev.azure.com/acme/PMIS/_git/pmis"));   // the token must not travel over plain http
        Assert.False(RepoSource.IsValid(Path.GetTempPath()));                            // a folder, but not a repository
    }

    [Fact]
    public void A_remote_is_cloned_once_and_then_follows_new_commits()
    {
        using var remote = TempRepo.WithSampleProject();

        RepoSource.CloneOrUpdate(remote.Root, pat: null, _clone);
        Assert.True(File.Exists(Path.Combine(_clone, "src", "Services", "InvoiceService.cs")));
        Assert.Equal(remote.Root, RepoSource.OriginUrl(_clone));

        remote.Write("src/Services/RetryPolicy.cs", "namespace Pmis.Services;\n\npublic class RetryPolicy { }\n");
        remote.Commit("Add retry policy", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        RepoSource.CloneOrUpdate(remote.Root, pat: null, _clone);

        Assert.True(File.Exists(Path.Combine(_clone, "src", "Services", "RetryPolicy.cs")));
    }

    [Fact]
    public void A_folder_is_used_where_it_is_and_the_probe_says_what_it_found()
    {
        using var repo = TempRepo.WithSampleProject();

        Assert.Equal(repo.Root, RepoSource.EnsureLocal(repo.Root, pat: null, out var warning));
        Assert.Null(warning);
        Assert.True(RepoSource.IsValid(repo.Root));

        Assert.Matches(@"^✓ Git repository on branch \w+, 6 tracked files\.$", RepoSource.Probe(repo.Root, null));
        Assert.StartsWith("✗ That folder is not a git repository", RepoSource.Probe(Path.GetTempPath(), null));
        Assert.StartsWith("✗ That is neither a folder", RepoSource.Probe(@"C:\no\such\folder", null));
        Assert.StartsWith("✗ Enter a repository", RepoSource.Probe(" ", null));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_clone)) return;
        foreach (var file in Directory.EnumerateFiles(_clone, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);   // git marks its object files read-only
        Directory.Delete(_clone, recursive: true);
    }
}
