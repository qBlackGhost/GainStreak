namespace GymApp.Client;

/// <summary>Where the session token lives between runs. The token is the stored credential: whoever has it is signed in.
/// Desktop uses <see cref="FileTokenStore"/>; a mobile head will use the Keychain or Keystore.</summary>
public interface ITokenStore
{
    string? Get();
    void Set(string token);
    void Clear();
}

/// <summary>Keeps the token in memory only, for tests.</summary>
public sealed class MemoryTokenStore(string? token = null) : ITokenStore
{
    private volatile string? _token = token;
    public string? Get() => _token;
    public void Set(string token) => _token = token;
    public void Clear() => _token = null;
}

/// <summary>Keeps the token in one file, readable and writable only by the user on macOS and Linux (mode 600).
/// The file is written to a temporary name first and moved into place, so a crash never leaves half a token.</summary>
public sealed class FileTokenStore(string path) : ITokenStore
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly Lock _lock = new();
    private bool _loaded;
    private string? _token;

    public string Path { get; } = path;

    public string? Get()
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                _token = File.Exists(Path) ? File.ReadAllText(Path).Trim() is { Length: > 0 } t ? t : null : null;
                _loaded = true;
            }
            return _token;
        }
    }

    public void Set(string token)
    {
        lock (_lock)
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            var temp = Path + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerOnly;
            using (var stream = new FileStream(temp, options))
            using (var writer = new StreamWriter(stream))
                writer.Write(token);
            // UnixCreateMode only applies to a new file; a leftover temp file keeps its old mode
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, OwnerOnly);
            File.Move(temp, Path, overwrite: true);
            _token = token;
            _loaded = true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(Path)) File.Delete(Path);
            _token = null;
            _loaded = true;
        }
    }
}
