using Microsoft.Win32;

namespace JvDataCollector;

public static class LogonRegistration
{
    private const string ValueName = "KeibaDwh.Collector";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            key?.SetValue(ValueName, Command());
        }
        else
        {
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static void EnsureBackgroundArgument()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key?.GetValue(ValueName) is not string current || current.Contains("--background", StringComparison.Ordinal))
        {
            return;
        }

        key.SetValue(ValueName, Command());
    }

    private static string Command() => $"\"{Application.ExecutablePath}\" --background";
}

public static class IconFactory
{
    public static Icon Circle(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 2, 2, 28, 28);
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
