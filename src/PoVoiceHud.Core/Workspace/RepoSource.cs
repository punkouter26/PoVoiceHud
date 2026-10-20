using System.Text.RegularExpressions;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace PoVoiceHud.Core.Workspace;

/// <summary>
/// Where the repository comes from: a folder on this PC that holds a git repository, or an https git URL
/// (an Azure DevOps repo) that is cloned into the app's own cache and refreshed on every start.
/// Everything downstream — history, schema, docs, code research — reads the local copy either way.
/// </summary>
public static partial class RepoSource
{
    /// <summary>Clones of remote repositories live here, one folder per repository.</summary>
    public static string CacheDir { get; } = Path.Combine(Config.AppPaths.DataDir, "repos");

    /// <summary>An https git URL. Plain http is refused: the access token travels with every request.</summary>
    public static bool IsRemote(string? location) => IsHttps(location, out _);

    /// <summary>A PAT is a password: it only ever goes to an https URL.</summary>
    public static bool IsHttps(string? url, out Uri uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>A folder or an https URL — the two things the Repository setting accepts.</summary>
    public static bool IsValid(string? location) =>
        IsRemote(location) || (!string.IsNullOrWhiteSpace(location) && Directory.Exists(location) && Repository.IsValid(location));

    /// <summary>
    /// Organization, project and repository from an Azure DevOps git URL:
    /// https://dev.azure.com/{org}/{project}/_git/{repo} (with or without "{org}@") or https://{org}.visualstudio.com/{project}/_git/{repo}.
    /// </summary>
    public static bool TryParseAzdo(string? url, out Uri organization, out string project, out string repository)
    {
        (organization, project, repository) = (null!, "", "");
        var match = AzdoUrl().Match(url ?? "");
        if (!match.Success) return false;
        organization = match.Groups["legacy"].Success
            ? new Uri($"https://{match.Groups["legacy"].Value}.visualstudio.com")
            : new Uri($"https://dev.azure.com/{match.Groups["org"].Value}");
        project = Uri.UnescapeDataString(match.Groups["project"].Value);
        repository = Uri.UnescapeDataString(match.Groups["repo"].Value);
        return true;
    }

    /// <summary>The URL a local repository was cloned from, if it has an "origin".</summary>
    public static string? OriginUrl(string root)
    {
        using var repo = new Repository(root);
        return repo.Network.Remotes["origin"]?.Url;
    }

    /// <summary>
    /// Returns the folder to read from. A folder is returned as it is. A URL is cloned on first use and afterwards
    /// fetched and moved to the remote's latest commit; if that refresh fails (offline, expired token) the existing
    /// copy is still returned and <paramref name="warning"/> says it may be out of date.
    /// </summary>
    public static string EnsureLocal(string location, string? pat, out string? warning)
    {
        warning = null;
        if (!IsRemote(location)) return location;

        var target = Path.Combine(CacheDir, CacheName(new Uri(location)));
        try
        {
            CloneOrUpdate(location, pat, target);
        }
        catch (LibGit2SharpException e) when (Repository.IsValid(target))
        {
            warning = $"repository not refreshed ({e.Message.TrimEnd('.')}), using the copy from last time";
        }
        return target;
    }

    /// <summary>Clones <paramref name="url"/> into <paramref name="target"/>, or brings an existing clone there up to date.</summary>
    public static void CloneOrUpdate(string url, string? pat, string target)
    {
        var credentials = Credentials(pat);
        if (Repository.IsValid(target))
        {
            using var repo = new Repository(target);
            Commands.Fetch(repo, "origin", [], new FetchOptions { CredentialsProvider = credentials }, null);
            // Nobody edits the cached clone, so there is nothing to merge: just move to where the remote branch is now.
            if (repo.Head.TrackedBranch?.Tip is { } tip) repo.Reset(ResetMode.Hard, tip);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var options = new CloneOptions();
        options.FetchOptions.CredentialsProvider = credentials;
        Repository.Clone(url, target, options);
    }

    /// <summary>For the Test button: one line saying whether the repository can be reached, starting with ✓ or ✗.</summary>
    public static string Probe(string? location, string? pat)
    {
        if (string.IsNullOrWhiteSpace(location)) return "✗ Enter a repository URL or choose a folder first.";
        try
        {
            if (IsRemote(location))
            {
                var branches = Repository.ListRemoteReferences(location, Credentials(pat)).Count(r => r.CanonicalName.StartsWith("refs/heads/", StringComparison.Ordinal));
                return $"✓ Connected: {branches} {(branches == 1 ? "branch" : "branches")} found.";
            }
            if (!Directory.Exists(location)) return "✗ That is neither a folder on this PC nor an https:// URL.";
            if (!Repository.IsValid(location)) return "✗ That folder is not a git repository.";
            using var repo = new Repository(location);
            return $"✓ Git repository on branch {repo.Head.FriendlyName}, {repo.Index.Count} tracked files.";
        }
        catch (LibGit2SharpException e)
        {
            // Azure DevOps answers a missing or expired token with a sign-in page, which libgit2 reports in its own words.
            return $"✗ Could not connect: {e.Message.TrimEnd('.')}. Check the URL and the access token.";
        }
    }

    // Azure DevOps takes any user name with a PAT as the password. Without a token the request goes out anonymously.
    private static CredentialsHandler? Credentials(string? pat) =>
        string.IsNullOrEmpty(pat) ? null : (_, _, _) => new UsernamePasswordCredentials { Username = "pat", Password = pat };

    /// <summary>"dev.azure.com-acme-PMIS-pmis" from the URL: readable, and unique per repository.</summary>
    private static string CacheName(Uri url) =>
        Unsafe().Replace(string.Join('-', new[] { url.Host }.Concat(url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != "_git"))), "_");

    [GeneratedRegex(@"^https://(?:(?:[^@/]+@)?dev\.azure\.com/(?<org>[^/]+)|(?<legacy>[^./]+)\.visualstudio\.com(?:/DefaultCollection)?)/(?<project>[^/]+)/_git/(?<repo>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AzdoUrl();

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex Unsafe();
}
