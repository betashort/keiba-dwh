namespace DwhLoader;

public static class LayoutCatalog
{
    public static readonly IReadOnlyDictionary<string, int> Length = new Dictionary<string, int>
    {
        ["TK"] = 21657,
        ["RA"] = 1272,
        ["SE"] = 555,
        ["HR"] = 719,
        ["H1"] = 28955,
        ["H6"] = 102890,
        ["O1"] = 962,
        ["O2"] = 2042,
        ["O3"] = 2654,
        ["O4"] = 4031,
        ["O5"] = 12293,
        ["O6"] = 83285,
        ["UM"] = 1609,
        ["KS"] = 4173,
        ["CH"] = 3862,
        ["BR"] = 545,
        ["BN"] = 477,
        ["HN"] = 251,
        ["SK"] = 208,
        ["RC"] = 501,
        ["HC"] = 60,
        ["HS"] = 200,
        ["HY"] = 123,
        ["YS"] = 382,
        ["BT"] = 6889,
        ["CS"] = 6829,
        ["DM"] = 303,
        ["TM"] = 141,
        ["WF"] = 7215,
        ["JG"] = 80,
        ["WC"] = 105,
        ["WH"] = 847,
        ["WE"] = 42,
        ["AV"] = 78,
        ["JC"] = 161,
        ["TC"] = 45,
        ["CC"] = 50,
    };

    public static readonly IReadOnlyDictionary<string, int> OldLength = new Dictionary<string, int>
    {
        ["BR"] = 537,
        ["HN"] = 245,
        ["SK"] = 178,
        ["HS"] = 196,
        ["BT"] = 6887,
        ["UM"] = 1577,
    };

    private static readonly IReadOnlyDictionary<string, (int Old, int New)[]> Pads = BuildPads();

    public static bool TryPrepare(string recordId, byte[] payload, out byte[] bytes, out short layoutRev)
    {
        bytes = payload;
        layoutRev = 0;
        if (!Length.TryGetValue(recordId, out var length))
        {
            return false;
        }

        if (payload.Length == length)
        {
            layoutRev = OldLength.ContainsKey(recordId) ? (short)1 : (short)0;
            return true;
        }

        if (OldLength.TryGetValue(recordId, out var oldLength) && payload.Length == oldLength)
        {
            bytes = Pad(payload, Pads[recordId]);
            layoutRev = 0;
            return true;
        }

        return false;
    }

    public static byte[] Pad(byte[] source, (int Old, int New)[] fields)
    {
        var size = 0;
        foreach (var field in fields)
        {
            size += field.New;
        }

        var result = new byte[size];
        var sourceAt = 0;
        var destAt = 0;
        foreach (var field in fields)
        {
            if (sourceAt + field.Old > source.Length)
            {
                throw new RecordFormatException("構造体長の不一致");
            }

            Buffer.BlockCopy(source, sourceAt, result, destAt, field.Old);
            for (var extra = field.Old; extra < field.New; extra++)
            {
                result[destAt + extra] = (byte)' ';
            }

            sourceAt += field.Old;
            destAt += field.New;
        }

        if (sourceAt != source.Length)
        {
            throw new RecordFormatException("構造体長の不一致");
        }

        return result;
    }

    private static Dictionary<string, (int Old, int New)[]> BuildPads()
    {
        (int, int)[] Pair(params int[] widths)
        {
            var pairs = new (int, int)[widths.Length / 2];
            for (var i = 0; i < pairs.Length; i++)
            {
                pairs[i] = (widths[i * 2], widths[(i * 2) + 1]);
            }

            return pairs;
        }

        var um = new List<(int, int)>
        {
            (11, 11), (10, 10), (1, 1), (8, 8), (8, 8), (8, 8), (36, 36), (36, 36), (60, 60),
            (1, 1), (19, 19), (2, 2), (1, 1), (1, 1), (2, 2),
        };
        for (var i = 0; i < 14; i++)
        {
            um.Add((8, 10));
            um.Add((36, 36));
        }

        um.AddRange([
            (1, 1), (5, 5), (8, 8), (20, 20), (6, 8), (70, 72), (20, 20), (6, 6), (64, 64),
            (54, 54), (18, 18), (18, 18), (126, 126), (216, 216), (108, 108), (12, 12), (3, 3), (2, 2),
        ]);

        var sk = new List<(int, int)>
        {
            (11, 11), (10, 10), (8, 8), (1, 1), (1, 1), (2, 2), (1, 1), (4, 4), (6, 8), (20, 20),
        };
        for (var i = 0; i < 14; i++)
        {
            sk.Add((8, 10));
        }

        sk.Add((2, 2));
        return new Dictionary<string, (int Old, int New)[]>
        {
            ["BR"] = Pair(11, 11, 6, 8, 70, 72, 70, 72, 70, 72, 168, 168, 20, 20, 120, 120, 2, 2),
            ["HN"] = Pair(11, 11, 8, 10, 8, 8, 10, 10, 1, 1, 36, 36, 40, 40, 80, 80, 4, 4, 1, 1, 1, 1, 2, 2, 1, 1, 4, 4, 20, 20, 8, 10, 8, 10, 2, 2),
            ["SK"] = [.. sk],
            ["HS"] = Pair(11, 11, 10, 10, 8, 10, 8, 10, 4, 4, 6, 6, 40, 40, 80, 80, 8, 8, 8, 8, 1, 1, 10, 10, 2, 2),
            ["BT"] = Pair(11, 11, 8, 10, 30, 30, 36, 36, 6800, 6800, 2, 2),
            ["UM"] = [.. um],
        };
    }
}
