using LibGit2Sharp;

namespace PoVoiceHud.Core.Workspace;

/// <summary>The active repository: keeps a <see cref="SymbolIndex"/> fresh via a debounced file watcher.</summary>
public sealed class Workspace : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;

    public Workspace(string root)
    {
        if (!Repository.IsValid(root)) throw new ArgumentException($"'{root}' is not a git repository.", nameof(root));
        Root = Path.GetFullPath(root);
        Index = SymbolIndex.Build(Root);

        _debounce = new Timer(_ => Rebuild());
        _watcher = new FileSystemWatcher(Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.EnableRaisingEvents = true;
    }

    public string Root { get; }
    public SymbolIndex Index { get; private set; }

    public event Action? Changed;

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (IsRelevant(e.FullPath)) _debounce.Change(500, Timeout.Infinite);
    }

    /// <summary>Builds and fetches churn thousands of files under bin/obj/.git; only tracked-file changes move the index.</summary>
    private static bool IsRelevant(string fullPath)
    {
        var relative = fullPath.Replace('\\', '/');
        if (relative.EndsWith("/.git/index", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var noise in new[] { "/.git/", "/bin/", "/obj/", "/node_modules/" })
            if (relative.Contains(noise, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void Rebuild()
    {
        try
        {
            Index = SymbolIndex.Build(Root);
            Changed?.Invoke();
        }
        catch (Exception e) when (e is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            // .git mid-write or a file locked/deleted by the editor; this runs on a timer thread, so never let it escape.
            // The next change event retries.
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
