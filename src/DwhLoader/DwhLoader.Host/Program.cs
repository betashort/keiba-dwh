using System.Runtime.InteropServices;
using System.Text;

namespace DwhLoader;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--set-password")
        {
            return LoaderApp.SetPasswordFromConsole();
        }

        using var mutex = LoaderApp.AcquireMutex();
        if (mutex is null)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();
        return LoaderApp.RunInteractive();
    }
}

public static class LoaderApp
{
    public const string MutexName = @"Local\KeibaDwh.Loader";

    public static int RunInteractive()
    {
        var configs = new LoaderConfigStore();
        var passwords = new PasswordStore();
        var log = new RollingFileLog();
        var password = passwords.Load();
        if (!string.IsNullOrEmpty(password))
        {
            log.NotePassword(password);
        }

        var settings = configs.Load();
        if (!settings.Runnable(!string.IsNullOrEmpty(password)))
        {
            using var setup = new SettingsForm(settings, !string.IsNullOrEmpty(password), true, input => Save(configs, passwords, log, null, input));
            if (setup.ShowDialog() != DialogResult.OK)
            {
                return 1;
            }

            settings = configs.Load();
            password = passwords.Load();
        }

        var session = new LoaderSession(log, settings, password);
        Application.Run(new StatusForm(
            session,
            input => Save(configs, passwords, log, session, input),
            () => configs.Load(),
            () => !string.IsNullOrEmpty(passwords.Load())));
        session.Dispose();
        return 0;
    }

    public static int SetPasswordFromConsole()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            return 1;
        }

        var password = ReadSecret(() => Console.ReadKey(intercept: true));
        Console.WriteLine();
        new PasswordStore().Save(password);
        return 0;
    }

    public static string ReadSecret(Func<ConsoleKeyInfo> read)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = read();
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }

        return buffer.ToString();
    }

    public static Mutex AcquireMutex() => AcquireMutex(MutexName);

    public static Mutex AcquireMutex(string name)
    {
        try
        {
            var mutex = new Mutex(true, name, out var created);
            if (created)
            {
                return mutex;
            }

            mutex.Dispose();
            return null;
        }
        catch (AbandonedMutexException)
        {
            return new Mutex(true, name, out _);
        }
    }

    public static string Save(
        LoaderConfigStore configs,
        PasswordStore passwords,
        RollingFileLog log,
        LoaderSession session,
        SettingsInput input)
    {
        if (!int.TryParse((input.Port ?? "").Trim(), out var port) || port is < 1 or > 65535)
        {
            return "ポートは 1 から 65535 の整数です";
        }

        if (string.IsNullOrWhiteSpace(input.DataRoot)
            || string.IsNullOrWhiteSpace(input.Host)
            || string.IsNullOrWhiteSpace(input.Database)
            || string.IsNullOrWhiteSpace(input.Username))
        {
            return "保存先、ホスト、データベース、ユーザ名を入れてください";
        }

        var existing = passwords.Load();
        if (string.IsNullOrEmpty(input.Password) && string.IsNullOrEmpty(existing))
        {
            return "パスワードがありません";
        }

        var settings = new LoaderSettings
        {
            DataRoot = input.DataRoot.Trim(),
            Host = input.Host.Trim(),
            Port = port,
            Database = input.Database.Trim(),
            Username = input.Username.Trim(),
        };
        configs.Save(settings);
        var password = existing;
        if (!string.IsNullOrEmpty(input.Password))
        {
            passwords.Save(input.Password);
            password = input.Password;
            log?.NotePassword(password);
        }

        session?.Use(settings, password);
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;
}
