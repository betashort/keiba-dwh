using Microsoft.Win32;

namespace JvDataCollector;

public static class JvRegistration
{
    public static bool Is64BitRegistered =>
        Type.GetTypeFromProgID("JVDTLab.JVLink", throwOnError: false) is not null;

    public static bool Is32BitInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Classes\CLSID\{2AB1774D-0C41-11D7-916F-0003479BEB3F}");
            return key is not null;
        }
    }
}
