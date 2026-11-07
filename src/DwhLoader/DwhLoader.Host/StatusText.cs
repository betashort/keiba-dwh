namespace DwhLoader;

public sealed class FileLine
{
    public FileLine(long id, string dataspec, string result, int? count, string reason, DateTimeOffset? retryUntil)
    {
        Id = id;
        Dataspec = dataspec;
        Result = result;
        Count = count;
        Reason = reason;
        RetryUntil = retryUntil;
    }

    public long Id { get; }

    public string Dataspec { get; }

    public string Result { get; }

    public int? Count { get; }

    public string Reason { get; }

    public DateTimeOffset? RetryUntil { get; }

    public string ReasonText(DateTimeOffset now)
    {
        if (Result != "再試行待ち" || RetryUntil is null)
        {
            return Reason ?? "";
        }

        var left = (int)Math.Ceiling((RetryUntil.Value - now).TotalSeconds);
        if (left < 0)
        {
            left = 0;
        }

        return $"{Reason}   あと {left} 秒";
    }
}

public sealed class StatusSnapshot
{
    public string Headline { get; init; } = "待機";

    public string Detail { get; init; } = "";

    public string Warehouse { get; init; } = "確認していません";

    public string Ob14 { get; init; } = "前回の写しのまま";

    public string Cycle { get; init; } = "";

    public bool ShowList { get; init; }

    public bool ShowOb14 { get; init; } = true;

    public IReadOnlyList<FileLine> Files { get; init; } = [];
}

public static class StatusText
{
    public static string Ob14(IReadOnlyList<Ob14Member> members, bool kept)
    {
        if (kept || members is null)
        {
            return "前回の写しのまま";
        }

        if (members.Count == 0)
        {
            return "メンバー 0";
        }

        return string.Join(
            " / ",
            members
                .GroupBy(member => (member.MeetingDay, member.Generation))
                .OrderBy(group => group.Key.MeetingDay)
                .Select(group => $"{group.Key.MeetingDay:yyyy-MM-dd}   世代 {group.Key.Generation}   メンバー {group.Count()}"));
    }

    public static StatusSnapshot Idle() => new()
    {
        Headline = "待機",
        Detail = "この周回は書いていない。5 秒後に次を見る",
        Warehouse = "確認していません",
        Ob14 = "前回の写しのまま",
        Cycle = "0 件",
        ShowList = false,
    };

    public static StatusSnapshot Waiting(string ob14, IReadOnlyList<FileLine> files) => new()
    {
        Headline = "待機",
        Detail = "この周回は書いていない。5 秒後に次を見る",
        Warehouse = "接続できている",
        Ob14 = ob14,
        Cycle = "0 件",
        ShowList = true,
        Files = files,
    };

    public static StatusSnapshot Following(int wrote, string ob14, IReadOnlyList<FileLine> files) => new()
    {
        Headline = "追随",
        Detail = "",
        Warehouse = "接続できている",
        Ob14 = ob14,
        Cycle = $"{wrote} 件書いた。残りがあるので待たない",
        ShowList = true,
        Files = files,
    };

    public static StatusSnapshot Writing(CompleteFile file, int wrote, bool moreAfter, string ob14, IReadOnlyList<FileLine> files) => new()
    {
        Headline = "投入中",
        Detail = $"id {file.Id}   {file.Dataspec}",
        Warehouse = "接続できている",
        Ob14 = ob14,
        Cycle = moreAfter ? $"{wrote} 件書いた。残りがあるので待たない" : $"{wrote} 件書いた",
        ShowList = true,
        Files = files,
    };

    public static StatusSnapshot LedgerDown() => new()
    {
        Headline = "台帳を開けません",
        Detail = "ファイルは失敗にしていません。5 秒後に周回をやり直します",
        Warehouse = "確認していません",
        Ob14 = "前回の写しのまま",
        ShowList = false,
    };

    public static StatusSnapshot PostgresDown() => new()
    {
        Headline = "PostgreSQL に届きません",
        Detail = "ファイルは失敗にしていません。5 秒後に周回をやり直します",
        Warehouse = "届かない",
        Ob14 = "前回の写しのまま",
        ShowList = false,
    };

    public static StatusSnapshot SchemaMissing() => new()
    {
        Headline = "スキーマ未適用",
        Detail = "jv.ingest_file がありません。5 秒ごとに確認します\r\n周回は始めていません",
        Warehouse = "接続できている",
        Ob14 = "",
        ShowList = false,
        ShowOb14 = false,
    };

    public static string Describe(StatusSnapshot snapshot)
    {
        var files = snapshot.ShowList
            ? string.Join("\n", snapshot.Files.Select(line => $"{line.Id} {line.Dataspec} {line.Result} {line.Reason}"))
            : "";
        return string.Join("\n", snapshot.Headline, snapshot.Detail, snapshot.Warehouse, snapshot.Ob14, snapshot.Cycle, files);
    }
}
