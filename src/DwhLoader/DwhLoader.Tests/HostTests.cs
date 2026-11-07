using System.Text.Json;
using DwhLoader;
using Microsoft.Win32;

namespace DwhLoader.Tests;

public sealed class HostTests
{
    [Fact]
    public void Config_KeepsPasswordOutsideJson_AndDropsUnknownKeys()
    {
        var root = Temp();
        var configPath = Path.Combine(root, "config.json");
        var passwordPath = Path.Combine(root, "db-password.bin");
        var collector = Path.Combine(root, "collector.json");
        File.WriteAllText(collector, """{"dataRoot":"D:\\KeibaData","launchAtLogon":true}""");
        File.WriteAllText(configPath, """
            {
              "dataRoot": "D:\\KeibaData",
              "host": "dbhost.example",
              "port": 5432,
              "database": "loader_it",
              "username": "keiba_loader",
              "token": "super-secret-value"
            }
            """);
        var configs = new LoaderConfigStore(configPath);
        var passwords = new PasswordStore(passwordPath);
        var loaded = configs.Load();
        Assert.Equal(["token"], loaded.UnknownKeys);
        Assert.False(loaded.Runnable(false));
        var error = LoaderApp.Save(
            configs,
            passwords,
            null,
            null,
            new SettingsInput("D:\\KeibaData", "dbhost.example", "5432", "loader_it", "keiba_loader", ""));
        Assert.Equal("パスワードがありません", error);
        Assert.False(File.Exists(passwordPath));
        error = LoaderApp.Save(
            configs,
            passwords,
            new RollingFileLog(Path.Combine(root, "logs")),
            null,
            new SettingsInput("D:\\KeibaData", "dbhost.example", "5432", "loader_it", "keiba_loader", "loader-secret-value"));
        Assert.Null(error);
        using var saved = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.Equal(
            ["dataRoot", "host", "port", "database", "username"],
            saved.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.DoesNotContain("loader-secret-value", File.ReadAllText(configPath));
        Assert.DoesNotContain("password", File.ReadAllText(configPath), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("loader-secret-value", passwords.Load());
        Assert.Equal("""{"dataRoot":"D:\\KeibaData","launchAtLogon":true}""", File.ReadAllText(collector));
        error = LoaderApp.Save(
            configs,
            passwords,
            null,
            null,
            new SettingsInput("D:\\KeibaData", "dbhost.example", "5432", "loader_it", "keiba_loader", ""));
        Assert.Null(error);
        Assert.Equal("loader-secret-value", passwords.Load());
        Assert.DoesNotContain("super-secret-value", string.Join(",", loaded.UnknownKeys));
    }

    [Fact]
    public void Password_IsReadWithoutEchoingTheValueIntoTheStoreName()
    {
        var keys = "next-secret\r".Select(value => value == '\r'
            ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            : new ConsoleKeyInfo(value, ConsoleKey.A, false, false, false)).ToArray();
        var index = 0;
        var secret = LoaderApp.ReadSecret(() => keys[index++]);
        var root = Temp();
        var passwords = new PasswordStore(Path.Combine(root, "db-password.bin"));
        passwords.Save(secret);
        Assert.Equal("next-secret", passwords.Load());
        Assert.Equal(0, secretsSeenInFileName(root));
    }

    [Fact]
    public void Log_ScrubsPassword_AndThrottlesTheSameReason()
    {
        var root = Temp();
        var log = new RollingFileLog(root);
        log.NotePassword("loader-secret-value");
        log.Info("Host=dbhost.example;Password=loader-secret-value;Database=loader_it");
        log.FileIssue(4, "RACE", 9, "RA", "HashMismatchException ハッシュ不一致");
        log.FileIssue(4, "RACE", 9, "RA", "HashMismatchException ハッシュ不一致");
        var old = Path.Combine(root, "loader-20000101.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-15));
        log.Info("rotate");
        var text = string.Join("\n", Directory.GetFiles(root, "loader-*.log").Select(File.ReadAllText));
        Assert.DoesNotContain("loader-secret-value", text);
        Assert.Contains("password=***", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Count(text, "ハッシュ不一致"));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Windows_DoNotShowThePassword_AndStatusHidesTheConnection()
    {
        Samples.Sta(() =>
        {
            var settings = new LoaderSettings
            {
                DataRoot = @"D:\KeibaData",
                Host = "dbhost.example",
                Port = 5432,
                Database = "loader_it",
                Username = "keiba_loader",
                UnknownKeys = ["token"],
            };
            using var setup = new SettingsForm(settings, true, true, _ => null);
            var shown = setup.VisibleText();
            Assert.Contains("dbhost.example", shown);
            Assert.Contains("5432", shown);
            Assert.Contains("loader_it", shown);
            Assert.Contains("keiba_loader", shown);
            Assert.Contains("token", setup.UnknownText);
            Assert.DoesNotContain("super-secret-value", shown);
            Assert.Equal("", setup.PasswordText);
            Assert.DoesNotContain("loader-secret-value", shown);
            Assert.Contains("ログオン時には起動しません。", shown);

            var session = new LoaderSession(new RollingFileLog(Temp()), settings, "loader-secret-value");
            using var status = new StatusForm(session, _ => null, () => settings, () => true);
            status.Apply(StatusText.Waiting(
                "2026-11-08   世代 4   メンバー 12",
                [new FileLine(41, "RACE", "書けた", 2, "", null)]));
            var text = status.VisibleText();
            Assert.Contains("待機", text);
            Assert.Contains("接続できている", text);
            Assert.Contains("2026-11-08", text);
            Assert.Contains("世代 4", text);
            Assert.DoesNotContain("dbhost.example", text);
            Assert.DoesNotContain("5432", text);
            Assert.DoesNotContain("loader_it", text);
            Assert.DoesNotContain("keiba_loader", text);
            Assert.DoesNotContain("loader-secret-value", text);
            Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
            status.Apply(StatusText.Waiting(
                "前回の写しのまま",
                [new FileLine(41, "RACE", "再試行待ち", null, "HashMismatchException ハッシュ不一致", DateTimeOffset.UtcNow.AddSeconds(3))]));
            Assert.Contains("あと", status.VisibleText());
        });
    }

    [Fact]
    public void Windows_FitTheirTextWithoutOverlap()
    {
        Samples.Sta(() =>
        {
            var settings = new LoaderSettings
            {
                DataRoot = @"D:\KeibaData",
                Host = "localhost",
                Port = 5432,
                Database = "keiba",
                Username = "keiba_loader",
            };
            using var setup = new SettingsForm(settings, false, true, _ => null);
            AssertFits(setup);
            using var edit = new SettingsForm(settings, true, false, _ => null);
            AssertFits(edit);
            using var session = new LoaderSession(new RollingFileLog(Temp()), settings, "");
            using var status = new StatusForm(session, _ => null, () => settings, () => true);
            status.Apply(StatusText.Writing(
                new CompleteFile(1042, "RACE", "raw"),
                3,
                true,
                "2026-11-08   世代 4   メンバー 12",
                [
                    new FileLine(1042, "RACE", "書いています", 128, "", null),
                    new FileLine(1041, "0B15", "書けた", 40, "", null),
                    new FileLine(1038, "0B11", "再試行待ち", null, "ハッシュ不一致", DateTimeOffset.UtcNow.AddSeconds(3)),
                ]));
            AssertFits(status);
            status.Apply(StatusText.SchemaMissing());
            AssertFits(status);
            status.Apply(StatusText.LedgerDown());
            AssertFits(status);
        });
    }

    private static void AssertFits(Form form)
    {
        var visible = form.Controls.Cast<Control>().Where(control => control.Visible && control.Width > 0 && control.Height > 0).ToArray();
        for (var i = 0; i < visible.Length; i++)
        {
            for (var j = i + 1; j < visible.Length; j++)
            {
                var left = visible[i].Bounds;
                left.Inflate(-1, -1);
                Assert.False(left.IntersectsWith(visible[j].Bounds), $"{visible[i].Text} と {visible[j].Text} が重なる");
            }
        }

        foreach (var control in visible)
        {
            if (control is Label or Button && !string.IsNullOrEmpty(control.Text))
            {
                var needed = TextRenderer.MeasureText(control.Text, control.Font, new Size(control.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                Assert.True(needed.Width <= control.Width + 4, $"{control.Text} の幅 {needed.Width} > {control.Width}");
                Assert.True(needed.Height <= control.Height + 4, $"{control.Text} の高さ {needed.Height} > {control.Height}");
            }

            if (control is TextBox box && !string.IsNullOrEmpty(box.PlaceholderText))
            {
                var needed = TextRenderer.MeasureText(box.PlaceholderText, box.Font);
                Assert.True(needed.Width <= box.Width, box.PlaceholderText);
            }

            if (control is ListView list)
            {
                foreach (ColumnHeader column in list.Columns)
                {
                    var needed = TextRenderer.MeasureText(column.Text, list.Font).Width;
                    Assert.True(column.Width >= needed, column.Text);
                }

                foreach (ListViewItem item in list.Items)
                {
                    for (var index = 0; index < item.SubItems.Count; index++)
                    {
                        var text = item.SubItems[index].Text;
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        var needed = TextRenderer.MeasureText(text, list.Font).Width;
                        Assert.True(list.Columns[index].Width >= needed, text);
                    }
                }
            }
        }
    }

    [Fact]
    public void Source_DoesNotRegisterAtLogon_OrStartJvLink()
    {
        var root = SolutionRoot();
        var hits = new List<string>();
        foreach (var project in new[] { "DwhLoader.Host", "DwhLoader.Application", "DwhLoader.Storage" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, project), "*.cs"))
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (line.Contains("CurrentVersion", StringComparison.Ordinal) && line.Contains("Run", StringComparison.Ordinal))
                    {
                        hits.Add(line);
                    }

                    if (line.Contains("Process.Start", StringComparison.Ordinal) && !line.Contains("explorer.exe", StringComparison.Ordinal))
                    {
                        hits.Add(line);
                    }

                    if (line.Contains("JVLink", StringComparison.OrdinalIgnoreCase) || line.Contains("JV-Link", StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(line);
                    }
                }
            }

            var projectFile = File.ReadAllText(Path.Combine(root, project, project + ".csproj"));
            Assert.DoesNotContain("JvDataCollector", projectFile);
        }

        Assert.Equal("Local\\KeibaDwh.Loader", LoaderApp.MutexName);
        Assert.Empty(hits);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        foreach (var name in key?.GetValueNames() ?? [])
        {
            var value = key.GetValue(name)?.ToString() ?? "";
            Assert.DoesNotContain("DwhLoader", value, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SecondInstance_GetsNoMutex()
    {
        var name = @"Local\KeibaDwh.Loader.Test." + Guid.NewGuid().ToString("n");
        using var first = LoaderApp.AcquireMutex(name);
        using var second = LoaderApp.AcquireMutex(name);
        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void FollowLoop_WaitsOnlyWhenTheCycleWroteNothing()
    {
        var calls = 0;
        var sleeps = new List<TimeSpan>();
        FollowLoop.Run(
            () =>
            {
                calls++;
                if (calls == 1)
                {
                    return new CycleResult { Wrote = 2 };
                }

                if (calls == 2)
                {
                    return new CycleResult();
                }

                return new CycleResult { Stopped = true };
            },
            _ => { },
            (delay, _) => sleeps.Add(delay),
            CancellationToken.None);
        Assert.Equal([TimeSpan.FromSeconds(5)], sleeps);

        var schemaSleeps = 0;
        var schemaCalls = 0;
        FollowLoop.Run(
            () => ++schemaCalls == 1
                ? new CycleResult { SchemaMissing = true }
                : new CycleResult { Stopped = true },
            _ => { },
            (_, _) => schemaSleeps++,
            CancellationToken.None);
        Assert.Equal(1, schemaSleeps);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += value.Length;
        }

        return count;
    }

    private static int secretsSeenInFileName(string root) =>
        Directory.GetFiles(root).Count(file => file.Contains("next-secret", StringComparison.Ordinal));

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "keiba-dwh-host-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DwhLoader.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("ソリューションが見つかりません");
    }
}
