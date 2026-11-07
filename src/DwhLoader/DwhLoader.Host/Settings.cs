using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DwhLoader;

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeibaDwh",
        "Loader");

    public static string ConfigFile => Path.Combine(Root, "config.json");

    public static string PasswordFile => Path.Combine(Root, "db-password.bin");

    public static string LogDirectory => Path.Combine(Root, "logs");
}

public sealed class LoaderSettings
{
    public static readonly string[] KnownKeys = ["dataRoot", "host", "port", "database", "username"];

    public string DataRoot { get; init; } = "";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 5432;

    public string Database { get; init; } = "keiba";

    public string Username { get; init; } = "keiba_loader";

    public IReadOnlyList<string> UnknownKeys { get; init; } = [];

    public bool Invalid { get; init; }

    public bool Runnable(bool hasPassword) =>
        !Invalid
        && UnknownKeys.Count == 0
        && !string.IsNullOrWhiteSpace(DataRoot)
        && !string.IsNullOrWhiteSpace(Host)
        && Port is > 0 and <= 65535
        && !string.IsNullOrWhiteSpace(Database)
        && !string.IsNullOrWhiteSpace(Username)
        && hasPassword;
}

public sealed class LoaderConfigStore
{
    private readonly string _path;

    public LoaderConfigStore(string path = null) => _path = path ?? AppPaths.ConfigFile;

    public LoaderSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new LoaderSettings();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new LoaderSettings { Invalid = true };
            }

            var dataRoot = "";
            var host = "localhost";
            var port = 5432;
            var database = "keiba";
            var username = "keiba_loader";
            var unknown = new List<string>();
            var invalid = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "dataRoot":
                        dataRoot = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : Invalid(ref invalid);
                        break;
                    case "host":
                        host = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : Invalid(ref invalid);
                        break;
                    case "port":
                        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var parsed))
                        {
                            port = parsed;
                        }
                        else
                        {
                            invalid = true;
                        }

                        break;
                    case "database":
                        database = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : Invalid(ref invalid);
                        break;
                    case "username":
                        username = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : Invalid(ref invalid);
                        break;
                    default:
                        unknown.Add(property.Name);
                        break;
                }
            }

            return new LoaderSettings
            {
                DataRoot = dataRoot,
                Host = host,
                Port = port,
                Database = database,
                Username = username,
                UnknownKeys = unknown,
                Invalid = invalid,
            };
        }
        catch (JsonException)
        {
            return new LoaderSettings { Invalid = true };
        }
    }

    public void Save(LoaderSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = new JsonObject
        {
            ["dataRoot"] = settings.DataRoot ?? "",
            ["host"] = settings.Host ?? "",
            ["port"] = settings.Port,
            ["database"] = settings.Database ?? "",
            ["username"] = settings.Username ?? "",
        };
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, overwrite: true);
    }

    private static string Invalid(ref bool invalid)
    {
        invalid = true;
        return "";
    }
}

public sealed class PasswordStore
{
    private readonly string _path;

    public PasswordStore(string path = null) => _path = path ?? AppPaths.PasswordFile;

    public bool Exists => File.Exists(_path);

    public string Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Save(string password)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = Encoding.UTF8.GetBytes(password);
        try
        {
            File.WriteAllBytes(_path, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
