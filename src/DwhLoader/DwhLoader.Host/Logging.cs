using System.Text.RegularExpressions;

namespace DwhLoader;

public sealed class RollingFileLog : ILoadLog
{
    private static readonly Regex PasswordToken = new(@"(?i)(password|pwd)\s*=\s*[^;\s]*", RegexOptions.Compiled);
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Dictionary<string, DateTimeOffset> _seen = [];
    private string _password = "";

    public RollingFileLog(string directory = null) => _directory = directory ?? AppPaths.LogDirectory;

    public void NotePassword(string password) => _password = password ?? "";

    public void Info(string message) => Write("INFO", message);

    public void FileIssue(long fileId, string dataspec, long? seq, string recordId, string reason)
    {
        var key = fileId + "\n" + reason;
        lock (_gate)
        {
            if (_seen.TryGetValue(key, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(1))
            {
                return;
            }

            _seen[key] = DateTimeOffset.UtcNow;
        }

        var seqText = seq is null ? "" : $" seq {seq}";
        var recordText = string.IsNullOrEmpty(recordId) ? "" : $" {recordId}";
        Write("ERROR", $"{dataspec} id {fileId}{seqText}{recordText} {reason}");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {level} {Scrub(message)}{Environment.NewLine}";
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"loader-{DateTime.UtcNow:yyyyMMdd}.log");
            File.AppendAllText(path, line);
            foreach (var file in Directory.EnumerateFiles(_directory, "loader-*.log"))
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
        var text = PasswordToken.Replace(message ?? "", "password=***");
        if (!string.IsNullOrEmpty(_password))
        {
            text = text.Replace(_password, "***", StringComparison.Ordinal);
        }

        return text;
    }
}
