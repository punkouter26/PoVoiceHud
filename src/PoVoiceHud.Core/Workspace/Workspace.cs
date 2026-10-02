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

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => _debounce.Change(500, Timeout.Infinite);

    private void Rebuild()
    {
        try
        {
            Index = SymbolIndex.Build(Root);
            Changed?.Invoke();
        }
        catch (LibGit2SharpException)
        {
            // .git was mid-write; the next change event retries.
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
