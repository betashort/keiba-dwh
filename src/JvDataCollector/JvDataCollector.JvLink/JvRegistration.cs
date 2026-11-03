using Microsoft.Win32;

namespace JvDataCollector;

public static class JvRegistration
{
    public static bool IsInstalled
    {
        get
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var prog = hive.OpenSubKey(@"SOFTWARE\Classes\JVDTLab.JVLink\CLSID");
            var clsid = prog?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(clsid))
            {
                return false;
            }

            foreach (var leaf in new[] { "InprocServer32", "LocalServer32" })
            {
                using var key = hive.OpenSubKey($@"SOFTWARE\Classes\CLSID\{clsid}\{leaf}");
                var path = ServerPath(key?.GetValue(null) as string);
                if (ReadImage(path) == ImageKind.X86)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static bool Is64BitImage(string path) => ReadImage(path) == ImageKind.X64;

    private static ImageKind ReadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ImageKind.Unknown;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D)
            {
                return ImageKind.Unknown;
            }

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset > stream.Length - 6)
            {
                return ImageKind.Unknown;
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return ImageKind.Unknown;
            }

            return reader.ReadUInt16() switch
            {
                0x014C => ImageKind.X86,
                0x8664 => ImageKind.X64,
                _ => ImageKind.Unknown,
            };
        }
        catch (Exception)
        {
            return ImageKind.Unknown;
        }
    }

    private static string? ServerPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    private enum ImageKind
    {
        Unknown,
        X86,
        X64,
    }
}
