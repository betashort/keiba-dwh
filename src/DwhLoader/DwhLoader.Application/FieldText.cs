using System.Globalization;

namespace DwhLoader;

public static class FieldText
{
    public static bool IsInitial(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        foreach (var value in text)
        {
            if (value is not ('0' or ' ' or '\u3000'))
            {
                return false;
            }
        }

        return true;
    }

    public static string TrimEndSpaces(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var end = text.Length;
        while (end > 0 && text[end - 1] is ' ' or '\u3000')
        {
            end--;
        }

        return text[..end];
    }

    public static string NameOrNull(string text)
    {
        if (text is null || IsInitial(text))
        {
            return null;
        }

        var trimmed = TrimEndSpaces(text);
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static string CodeOrNull(string text)
    {
        if (text is null || IsInitial(text))
        {
            return null;
        }

        var trimmed = TrimEndSpaces(text);
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static int? IntOrNull(string text)
    {
        if (!TryDigits(text, out var digits))
        {
            return null;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    public static long? LongOrNull(string text)
    {
        if (!TryDigits(text, out var digits))
        {
            return null;
        }

        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    public static short? SmallOrNull(string text)
    {
        var number = IntOrNull(text);
        if (number is null || number < short.MinValue || number > short.MaxValue)
        {
            return null;
        }

        return (short)number;
    }

    public static DateOnly? CalendarOrNull(string ymd)
    {
        if (string.IsNullOrEmpty(ymd) || ymd.Length != 8 || ymd == "00000000" || ymd.Contains(' '))
        {
            return null;
        }

        return DateOnly.TryParseExact(ymd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    public static string KeyText(string text)
    {
        var trimmed = TrimEndSpaces(text ?? "");
        if (trimmed.Length == 0)
        {
            throw new RecordFormatException("値の形式が一致しません");
        }

        return trimmed;
    }

    public static short KeySmall(string text)
    {
        var number = SmallOrNull(text);
        if (number is null)
        {
            throw new RecordFormatException("値の形式が一致しません");
        }

        return number.Value;
    }

    public static int KeyInt(string text)
    {
        var number = IntOrNull(text);
        if (number is null)
        {
            throw new RecordFormatException("値の形式が一致しません");
        }

        return number.Value;
    }

    public static DateOnly KeyDate(string ymd)
    {
        var date = CalendarOrNull(ymd);
        if (date is null)
        {
            throw new RecordFormatException("値の形式が一致しません");
        }

        return date.Value;
    }

    public static int?[] Ints(params string[] values)
    {
        var result = new int?[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = IntOrNull(values[i]);
        }

        return result;
    }

    public static bool Blank(params string[] values)
    {
        foreach (var value in values)
        {
            if (!IsInitial(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDigits(string text, out string digits)
    {
        digits = "";
        if (string.IsNullOrEmpty(text) || IsInitial(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        foreach (var value in trimmed)
        {
            if (value is < '0' or > '9')
            {
                return false;
            }
        }

        digits = trimmed;
        return true;
    }
}

public sealed class RecordFormatException : Exception
{
    public RecordFormatException(string message) : base(message)
    {
    }
}

public sealed class HashMismatchException : Exception
{
    public HashMismatchException() : base("ハッシュ不一致")
    {
    }
}

public sealed class PayloadOffsetException : Exception
{
    public PayloadOffsetException() : base("読み位置がファイルの末尾を超えています")
    {
    }
}

public sealed class LedgerUnavailableException : Exception
{
    public LedgerUnavailableException(string message) : base(message)
    {
    }
}

public sealed class WarehouseUnavailableException : Exception
{
    public WarehouseUnavailableException(string message) : base(message)
    {
    }
}

public sealed class Ob14CopyException : Exception
{
    public Ob14CopyException(string message) : base(message)
    {
    }
}
