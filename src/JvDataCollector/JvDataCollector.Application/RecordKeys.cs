using System.Text;

namespace JvDataCollector;

public static class RecordKeys
{
    public static FrameClassification Classify(ReadOnlySpan<byte> payload)
    {
        var recordId = RecordId(payload);
        string? meetingDay = null;
        string? raceKey = null;
        if (recordId == "YS" && payload.Length >= 19 && Digits(payload.Slice(11, 8)))
        {
            meetingDay = Encoding.ASCII.GetString(payload.Slice(11, 8));
        }

        if (recordId == "RA" && payload.Length >= 27 && Digits(payload.Slice(11, 8)))
        {
            raceKey = Encoding.ASCII.GetString(payload.Slice(11, 16));
        }

        return new FrameClassification(recordId, meetingDay, raceKey);
    }

    public static string RecordId(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return "";
        }

        if (payload[0] is >= 32 and < 127 && payload[1] is >= 32 and < 127)
        {
            return Encoding.ASCII.GetString(payload[..2]);
        }

        return Convert.ToHexString(payload[..2]);
    }

    public static bool Digits(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return bytes.Length > 0;
    }
}
