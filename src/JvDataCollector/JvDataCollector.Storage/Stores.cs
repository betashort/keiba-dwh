using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JvDataCollector;

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeibaDwh",
        "Collector");

    public static string ConfigFile => Path.Combine(Root, "config.json");

    public static string ServiceKeyFile => Path.Combine(Root, "service-key.bin");

    public static string LogDirectory => Path.Combine(Root, "logs");
}

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public JsonConfigStore(string? path = null) => _path = path ?? AppPaths.ConfigFile;

    public CollectorConfig Load()
    {
        if (!File.Exists(_path))
        {
            return new CollectorConfig { Dataspecs = [.. DataspecCatalog.DefaultCodes] };
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<CollectorConfig>(json, Options)
            ?? new CollectorConfig { Dataspecs = [.. DataspecCatalog.DefaultCodes] };
    }

    public void Save(CollectorConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(config, Options);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }
}

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;

    public DpapiSecretStore(string? path = null) => _path = path ?? AppPaths.ServiceKeyFile;

    public bool Exists => File.Exists(_path);

    public string? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_path);
        var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Save(string serviceKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = Encoding.UTF8.GetBytes(serviceKey);
        try
        {
            var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_path, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}

public sealed class DriveDiskSpace : IDiskSpace
{
    public long AvailableBytes(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || !new DriveInfo(root).IsReady)
        {
            return 0;
        }

        return new DriveInfo(root).AvailableFreeSpace;
    }
}

public sealed class RollingFileLog : IAppLog
{
    private readonly object _gate = new();
    private readonly string _directory;
    private string? _secret;

    public RollingFileLog(string? directory = null) => _directory = directory ?? AppPaths.LogDirectory;

    public void NoteSecret(string? secret) => _secret = string.IsNullOrEmpty(secret) ? null : secret;

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {level} {Scrub(message)}{Environment.NewLine}";
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"collector-{DateTime.UtcNow:yyyyMMdd}.log");
            File.AppendAllText(path, line);
            foreach (var file in Directory.EnumerateFiles(_directory, "collector-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                {
                    File.Delete(file);
                }
            }
        }
    }

    private string Scrub(string message)
    {
        var text = message.Replace(SoftwareId.Value, "sid", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(_secret) && _secret.Length >= 8)
        {
            text = text.Replace(_secret, "***", StringComparison.Ordinal);
        }

        return text;
    }
}

public sealed class NullLog : IAppLog
{
    public void Info(string message)
    {
    }

    public void Error(string message)
    {
    }

    public void NoteSecret(string? secret)
    {
    }
}
