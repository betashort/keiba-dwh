namespace JvDataCollector;

public sealed record DataspecInfo(string Code, string Name);

public static class DataspecCatalog
{
    public static readonly DataspecInfo[] Stored =
    [
        new("TOKU", "特別登録馬"),
        new("RACE", "レース情報"),
        new("DIFF", "蓄積情報"),
        new("DIFN", "蓄積情報（拡張）"),
        new("BLOD", "血統情報"),
        new("BLDN", "血統情報（拡張）"),
        new("SNAP", "出走時点の着度数"),
        new("SNPN", "出走時点（拡張）"),
        new("SLOP", "坂路調教"),
        new("WOOD", "ウッドチップ調教"),
        new("YSCH", "開催スケジュール"),
        new("HOSE", "競走馬市場取引価格"),
        new("HOSN", "市場取引価格（拡張）"),
        new("HOYU", "馬名の意味由来"),
        new("COMM", "コース情報"),
        new("MING", "マイニング"),
    ];

    public static readonly DataspecInfo[] Realtime =
    [
        new("0B11", "速報馬体重"),
        new("0B12", "速報成績"),
        new("0B13", "速報タイム型マイニング"),
        new("0B14", "速報開催情報（一括）"),
        new("0B15", "速報レース情報"),
        new("0B16", "速報開催情報（指定）"),
        new("0B17", "速報対戦型マイニング"),
        new("0B20", "速報票数"),
        new("0B31", "速報オッズ（単複枠）"),
        new("0B32", "速報オッズ（馬連）"),
        new("0B33", "速報オッズ（ワイド）"),
        new("0B34", "速報オッズ（馬単）"),
        new("0B35", "速報オッズ（3連複）"),
        new("0B36", "速報オッズ（3連単）"),
        new("0B41", "時系列オッズ（単複枠）"),
        new("0B42", "時系列オッズ（馬連）"),
        new("0B51", "速報 WIN5"),
    ];

    public static readonly string[] OddsSweep =
    [
        "0B31", "0B32", "0B33", "0B34", "0B35", "0B36", "0B41", "0B42", "0B20", "0B51",
    ];

    public static IReadOnlyList<string> DefaultCodes => Stored.Select(static item => item.Code).ToArray();

    public static string NameOf(string code)
    {
        foreach (var item in Stored)
        {
            if (item.Code == code)
            {
                return item.Name;
            }
        }

        foreach (var item in Realtime)
        {
            if (item.Code == code)
            {
                return item.Name;
            }
        }

        return code;
    }
}
