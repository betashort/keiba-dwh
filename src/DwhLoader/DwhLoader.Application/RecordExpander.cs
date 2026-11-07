using System.Text;

namespace DwhLoader;

public static class RecordExpander
{
    private static readonly Encoding Sjis = CreateEncoding();

    public static List<DbRow> Expand(
        string recordId,
        string dataspec,
        byte[] payload,
        byte[] sha256,
        DateTimeOffset collected)
    {
        if (recordId == "CK" || !LayoutCatalog.Length.ContainsKey(recordId))
        {
            return [Unparsed(recordId, dataspec, sha256)];
        }

        if (!LayoutCatalog.TryPrepare(recordId, payload, out var bytes, out var rev))
        {
            throw new RecordFormatException("構造体長の不一致");
        }

        try
        {
            return Map(recordId, bytes, dataspec, rev, sha256, collected);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or DecoderFallbackException)
        {
            throw new RecordFormatException("構造体長の不一致");
        }
    }

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static string TextOf(byte[] bytes)
    {
        string text;
        try
        {
            text = Sjis.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new RecordFormatException("構造体長の不一致");
        }

        if (Sjis.GetByteCount(text) != bytes.Length)
        {
            throw new RecordFormatException("構造体長の不一致");
        }

        return text;
    }

    private static List<DbRow> Map(
        string recordId,
        byte[] bytes,
        string dataspec,
        short rev,
        byte[] sha,
        DateTimeOffset collected)
    {
        var text = TextOf(bytes);
        return recordId switch
        {
            "TK" => Tk(text, dataspec, rev, sha, collected),
            "RA" => Ra(text, dataspec, rev, sha, collected),
            "SE" => Se(text, dataspec, rev, sha, collected),
            "HR" => Hr(text, dataspec, rev, sha, collected),
            "H1" => H1(text, dataspec, rev, sha, collected),
            "H6" => H6(text, dataspec, rev, sha, collected),
            "O1" => O1(text, dataspec, rev, sha, collected),
            "O2" => O2(text, dataspec, rev, sha, collected),
            "O3" => O3(text, dataspec, rev, sha, collected),
            "O4" => O4(text, dataspec, rev, sha, collected),
            "O5" => O5(text, dataspec, rev, sha, collected),
            "O6" => O6(text, dataspec, rev, sha, collected),
            "DM" => Dm(text, dataspec, rev, sha, collected),
            "TM" => Tm(text, dataspec, rev, sha, collected),
            "WH" => Wh(text, dataspec, rev, sha, collected),
            "TC" => Tc(text, dataspec, rev, sha, collected),
            "CC" => Cc(text, dataspec, rev, sha, collected),
            "AV" => Av(text, dataspec, rev, sha, collected),
            "JC" => Jc(text, dataspec, rev, sha, collected),
            "YS" => Ys(text, dataspec, rev, sha, collected),
            "WE" => We(text, dataspec, rev, sha, collected),
            "WF" => Wf(text, dataspec, rev, sha, collected),
            "UM" => Um(text, dataspec, rev, sha, collected),
            "SK" => Sk(text, dataspec, rev, sha, collected),
            "HY" => Hy(text, dataspec, rev, sha, collected),
            "KS" => Ks(text, dataspec, rev, sha, collected),
            "CH" => Ch(text, dataspec, rev, sha, collected),
            "BR" => Br(text, dataspec, rev, sha, collected),
            "BN" => Bn(text, dataspec, rev, sha, collected),
            "HN" => Hn(text, dataspec, rev, sha, collected),
            "HC" => Hc(text, dataspec, rev, sha, collected),
            "WC" => Wc(text, dataspec, rev, sha, collected),
            "HS" => Hs(text, dataspec, rev, sha, collected),
            "CS" => Cs(text, dataspec, rev, sha, collected),
            "RC" => Rc(text, dataspec, rev, sha, collected),
            "JG" => Jg(text, dataspec, rev, sha, collected),
            "BT" => Bt(text, dataspec, rev, sha, collected),
            _ => [Unparsed(recordId, dataspec, sha)],
        };
    }

    private static DbRow Unparsed(string recordId, string dataspec, byte[] sha)
    {
        var row = new DbRow("unparsed");
        row.Add("record_id", recordId);
        row.Add("src_dataspec", dataspec);
        row.Add("raw_sha256", sha);
        return row;
    }

    private static DbRow Start(
        string table,
        JVData_Struct.RECORD_ID head,
        string dataspec,
        short rev,
        byte[] sha,
        DateTimeOffset collected,
        params (string Name, object Value)[] keys)
    {
        var ymd = head.MakeDate.Year + head.MakeDate.Month + head.MakeDate.Day;
        if (ymd.Length != 8 || head.DataKubun is not { Length: 1 })
        {
            throw new RecordFormatException("値の形式が一致しません");
        }

        var row = new DbRow(table);
        foreach (var key in keys)
        {
            row.Add(key.Name, key.Value);
        }

        row.Add("data_kubun", head.DataKubun);
        row.Add("make_ymd", ymd);
        row.Add("make_date", FieldText.CalendarOrNull(ymd));
        row.Add("src_dataspec", dataspec);
        row.Add("layout_rev", rev);
        row.Add("raw_sha256", sha);
        row.Add("collected_at", collected);
        return row;
    }

    private static List<(string Name, object Value)> KeysOf(DbRow row)
    {
        var keys = new List<(string Name, object Value)>();
        foreach (var column in row.Columns)
        {
            if (column.Name is "make_date" or "layout_rev" or "collected_at")
            {
                continue;
            }

            keys.Add(column);
        }

        return keys;
    }

    private static DbRow Child(List<(string Name, object Value)> keys, string table)
    {
        var row = new DbRow(table, true);
        foreach (var key in keys)
        {
            row.Add(key.Name, key.Value);
        }

        return row;
    }

    private static string Race(JVData_Struct.RACE_ID id)
    {
        var key = id.Year + id.MonthDay + id.JyoCD + id.Kaiji + id.Nichiji + id.RaceNum;
        if (key.Length != 16)
        {
            throw new RecordFormatException("値の形式が一致しません");
        }

        return key;
    }

    private static string Meeting(JVData_Struct.RACE_ID2 id)
    {
        var key = id.Year + id.MonthDay + id.JyoCD + id.Kaiji + id.Nichiji;
        if (key.Length != 14)
        {
            throw new RecordFormatException("値の形式が一致しません");
        }

        return key;
    }

    private static string Happyo(JVData_Struct.MDHM time) =>
        (time.Month ?? "") + (time.Day ?? "") + (time.Hour ?? "") + (time.Minute ?? "");

    private static string Ymd(JVData_Struct.YMD ymd) => ymd.Year + ymd.Month + ymd.Day;

    private static void SlotUmaban(
        List<DbRow> rows,
        List<(string Name, object Value)> keys,
        string table,
        string umaban,
        params (string Name, string Text)[] fields)
    {
        if (FieldText.Blank([umaban, .. fields.Select(field => field.Text)]))
        {
            return;
        }

        var number = FieldText.SmallOrNull(umaban);
        if (number is null)
        {
            return;
        }

        var row = Child(keys, table);
        row.Add("umaban", number.Value);
        foreach (var field in fields)
        {
            row.Add(field.Name, FieldText.IntOrNull(field.Text));
        }

        rows.Add(row);
    }

    private static void SlotKumi(
        List<DbRow> rows,
        List<(string Name, object Value)> keys,
        string table,
        string kumi,
        params (string Name, object Value)[] fields)
    {
        var texts = fields.Select(field => field.Value as string).ToArray();
        if (FieldText.IsInitial(kumi) && FieldText.Blank(texts))
        {
            return;
        }

        var key = FieldText.TrimEndSpaces(kumi ?? "");
        if (key.Length == 0)
        {
            return;
        }

        var row = Child(keys, table);
        row.Add("kumi", key);
        foreach (var field in fields)
        {
            row.Add(field.Name, field.Value is string text ? FieldText.IntOrNull(text) : field.Value);
        }

        rows.Add(row);
    }

    private static List<DbRow> Tk(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_TK_TOKUUMA();
        parsed.SetDataB(ref text);
        var row = Start("special_entry", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var horse in parsed.TokuUmaInfo)
        {
            if (FieldText.Blank(horse.Num, horse.KettoNum, horse.Bamei))
            {
                continue;
            }

            var seq = FieldText.SmallOrNull(horse.Num);
            if (seq is null)
            {
                continue;
            }

            var child = Child(keys, "special_entry_horse");
            child.Add("seq", seq.Value);
            child.Add("ketto_num", FieldText.CodeOrNull(horse.KettoNum));
            child.Add("bamei", FieldText.NameOrNull(horse.Bamei));
            rows.Add(child);
        }

        return rows;
    }

    private static List<DbRow> Ra(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_RA_RACE();
        parsed.SetDataB(ref text);
        var row = Start("race", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        var keys = KeysOf(row);
        row.Add("honsyo", FieldText.Ints(parsed.Honsyokin));
        row.Add("fuka", FieldText.Ints(parsed.Fukasyokin));
        row.Add("lap", FieldText.Ints(parsed.LapTime));
        var rows = new List<DbRow> { row };
        foreach (var corner in parsed.CornerInfo)
        {
            if (FieldText.Blank(corner.Corner, corner.Syukaisu, corner.Jyuni))
            {
                continue;
            }

            var number = FieldText.SmallOrNull(corner.Corner);
            if (number is null)
            {
                continue;
            }

            var child = Child(keys, "race_corner");
            child.Add("corner", number.Value);
            child.Add("syukaisu", FieldText.CodeOrNull(corner.Syukaisu));
            child.Add("jyuni", FieldText.CodeOrNull(corner.Jyuni));
            rows.Add(child);
        }

        return rows;
    }

    private static List<DbRow> Se(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_SE_RACE_UMA();
        parsed.SetDataB(ref text);
        var row = Start(
            "race_horse",
            parsed.head,
            dataspec,
            rev,
            sha,
            collected,
            ("race_key", Race(parsed.id)),
            ("umaban", FieldText.KeySmall(parsed.Umaban)),
            ("ketto_num", FieldText.KeyText(parsed.KettoNum)));
        var keys = KeysOf(row);
        row.Add("wakuban", FieldText.SmallOrNull(parsed.Wakuban));
        row.Add("bamei", FieldText.NameOrNull(parsed.Bamei));
        var rows = new List<DbRow> { row };
        for (var i = 0; i < parsed.ChakuUmaInfo.Length; i++)
        {
            var rival = parsed.ChakuUmaInfo[i];
            if (FieldText.Blank(rival.KettoNum, rival.Bamei))
            {
                continue;
            }

            var child = Child(keys, "race_horse_rival");
            child.Add("ord", (short)(i + 1));
            child.Add("rival_ketto", FieldText.CodeOrNull(rival.KettoNum));
            child.Add("bamei", FieldText.NameOrNull(rival.Bamei));
            rows.Add(child);
        }

        return rows;
    }

    private static List<DbRow> Hr(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_HR_PAY();
        parsed.SetDataB(ref text);
        var row = Start("payout", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        Pay(rows, keys, "tansyo", parsed.PayTansyo, item => (item.Umaban, item.Pay, item.Ninki));
        Pay(rows, keys, "fukusyo", parsed.PayFukusyo, item => (item.Umaban, item.Pay, item.Ninki));
        Pay(rows, keys, "wakuren", parsed.PayWakuren, item => (item.Umaban, item.Pay, item.Ninki));
        Pay(rows, keys, "umaren", parsed.PayUmaren, item => (item.Kumi, item.Pay, item.Ninki));
        Pay(rows, keys, "wide", parsed.PayWide, item => (item.Kumi, item.Pay, item.Ninki));
        Pay(rows, keys, "umatan", parsed.PayUmatan, item => (item.Kumi, item.Pay, item.Ninki));
        Pay(rows, keys, "sanrenpuku", parsed.PaySanrenpuku, item => (item.Kumi, item.Pay, item.Ninki));
        Pay(rows, keys, "sanrentan", parsed.PaySanrentan, item => (item.Kumi, item.Pay, item.Ninki));
        return rows;
    }

    private static void Pay<T>(
        List<DbRow> rows,
        List<(string Name, object Value)> keys,
        string bet,
        T[] items,
        Func<T, (string Kumi, string Pay, string Ninki)> read)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var (kumi, pay, ninki) = read(items[i]);
            if (FieldText.Blank(kumi, pay, ninki))
            {
                continue;
            }

            var key = FieldText.TrimEndSpaces(kumi);
            if (key.Length == 0)
            {
                continue;
            }

            var child = Child(keys, "payout_line");
            child.Add("bet", bet);
            child.Add("ord", (short)(i + 1));
            child.Add("kumi", key);
            child.Add("pay", FieldText.LongOrNull(pay));
            child.Add("ninki", FieldText.IntOrNull(ninki));
            rows.Add(child);
        }
    }

    private static List<DbRow> H1(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_H1_HYOSU_ZENKAKE();
        parsed.SetDataB(ref text);
        var row = Start("hyosu", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        Hyo(rows, keys, "hyosu_line", "tansyo", parsed.HyoTansyo, item => (item.Umaban, item.Hyo, item.Ninki));
        Hyo(rows, keys, "hyosu_line", "fukusyo", parsed.HyoFukusyo, item => (item.Umaban, item.Hyo, item.Ninki));
        Hyo(rows, keys, "hyosu_line", "wakuren", parsed.HyoWakuren, item => (item.Umaban, item.Hyo, item.Ninki));
        Hyo(rows, keys, "hyosu_line", "umaren", parsed.HyoUmaren, item => (item.Kumi, item.Hyo, item.Ninki));
        Hyo(rows, keys, "hyosu_line", "wide", parsed.HyoWide, item => (item.Kumi, item.Hyo, item.Ninki));
        Hyo(rows, keys, "hyosu_line", "umatan", parsed.HyoUmatan, item => (item.Kumi, item.Hyo, item.Ninki));
        Hyo(rows, keys, "hyosu_line", "sanrenpuku", parsed.HyoSanrenpuku, item => (item.Kumi, item.Hyo, item.Ninki));
        return rows;
    }

    private static List<DbRow> H6(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_H6_HYOSU_SANRENTAN();
        parsed.SetDataB(ref text);
        var row = Start("hyosu_sanrentan", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        Hyo(rows, keys, "hyosu_sanrentan_line", null, parsed.HyoSanrentan, item => (item.Kumi, item.Hyo, item.Ninki));
        return rows;
    }

    private static void Hyo<T>(
        List<DbRow> rows,
        List<(string Name, object Value)> keys,
        string table,
        string bet,
        T[] items,
        Func<T, (string Kumi, string Hyo, string Ninki)> read)
    {
        foreach (var item in items)
        {
            var (kumi, hyo, ninki) = read(item);
            if (FieldText.Blank(kumi, hyo, ninki))
            {
                continue;
            }

            var key = FieldText.TrimEndSpaces(kumi);
            if (key.Length == 0)
            {
                continue;
            }

            var child = Child(keys, table);
            if (bet is not null)
            {
                child.Add("bet", bet);
            }

            child.Add("kumi", key);
            child.Add("hyo", FieldText.LongOrNull(hyo));
            child.Add("ninki", FieldText.IntOrNull(ninki));
            rows.Add(child);
        }
    }

    private static List<DbRow> O1(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_O1_ODDS_TANFUKUWAKU();
        parsed.SetDataB(ref text);
        var row = OddsParent("odds_tanpuku", parsed.head, parsed.id, parsed.HappyoTime, dataspec, rev, sha, collected);
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.OddsTansyoInfo)
        {
            SlotUmaban(rows, keys, "odds_tanpuku_tan", item.Umaban, ("odds", item.Odds), ("ninki", item.Ninki));
        }

        foreach (var item in parsed.OddsFukusyoInfo)
        {
            if (FieldText.Blank(item.Umaban, item.OddsLow, item.OddsHigh, item.Ninki))
            {
                continue;
            }

            var number = FieldText.SmallOrNull(item.Umaban);
            if (number is null)
            {
                continue;
            }

            var child = Child(keys, "odds_tanpuku_fuku");
            child.Add("umaban", number.Value);
            child.Add("odds_low", FieldText.IntOrNull(item.OddsLow));
            child.Add("odds_high", FieldText.IntOrNull(item.OddsHigh));
            child.Add("ninki", FieldText.IntOrNull(item.Ninki));
            rows.Add(child);
        }

        foreach (var item in parsed.OddsWakurenInfo)
        {
            SlotKumi(rows, keys, "odds_tanpuku_waku", item.Kumi, ("odds", item.Odds), ("ninki", item.Ninki));
        }

        return rows;
    }

    private static List<DbRow> O2(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_O2_ODDS_UMAREN();
        parsed.SetDataB(ref text);
        var row = OddsParent("odds_umaren", parsed.head, parsed.id, parsed.HappyoTime, dataspec, rev, sha, collected);
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.OddsUmarenInfo)
        {
            SlotKumi(rows, keys, "odds_umaren_kumi", item.Kumi, ("odds", item.Odds), ("ninki", item.Ninki));
        }

        return rows;
    }

    private static List<DbRow> O3(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_O3_ODDS_WIDE();
        parsed.SetDataB(ref text);
        var row = OddsParent("odds_wide", parsed.head, parsed.id, parsed.HappyoTime, dataspec, rev, sha, collected);
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.OddsWideInfo)
        {
            if (FieldText.Blank(item.Kumi, item.OddsLow, item.OddsHigh, item.Ninki))
            {
                continue;
            }

            var key = FieldText.TrimEndSpaces(item.Kumi);
            if (key.Length == 0)
            {
                continue;
            }

            var child = Child(keys, "odds_wide_kumi");
            child.Add("kumi", key);
            child.Add("odds_low", FieldText.IntOrNull(item.OddsLow));
            child.Add("odds_high", FieldText.IntOrNull(item.OddsHigh));
            child.Add("ninki", FieldText.IntOrNull(item.Ninki));
            rows.Add(child);
        }

        return rows;
    }

    private static List<DbRow> O4(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_O4_ODDS_UMATAN();
        parsed.SetDataB(ref text);
        var row = OddsParent("odds_umatan", parsed.head, parsed.id, parsed.HappyoTime, dataspec, rev, sha, collected);
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.OddsUmatanInfo)
        {
            SlotKumi(rows, keys, "odds_umatan_kumi", item.Kumi, ("odds", item.Odds), ("ninki", item.Ninki));
        }

        return rows;
    }

    private static List<DbRow> O5(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_O5_ODDS_SANREN();
        parsed.SetDataB(ref text);
        var row = OddsParent("odds_sanrenpuku", parsed.head, parsed.id, parsed.HappyoTime, dataspec, rev, sha, collected);
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.OddsSanrenInfo)
        {
            SlotKumi(rows, keys, "odds_sanrenpuku_kumi", item.Kumi, ("odds", item.Odds), ("ninki", item.Ninki));
        }

        return rows;
    }

    private static List<DbRow> O6(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_O6_ODDS_SANRENTAN();
        parsed.SetDataB(ref text);
        var row = OddsParent("odds_sanrentan", parsed.head, parsed.id, parsed.HappyoTime, dataspec, rev, sha, collected);
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.OddsSanrentanInfo)
        {
            SlotKumi(rows, keys, "odds_sanrentan_kumi", item.Kumi, ("odds", item.Odds), ("ninki", item.Ninki));
        }

        return rows;
    }

    private static DbRow OddsParent(
        string table,
        JVData_Struct.RECORD_ID head,
        JVData_Struct.RACE_ID id,
        JVData_Struct.MDHM time,
        string dataspec,
        short rev,
        byte[] sha,
        DateTimeOffset collected) =>
        Start(table, head, dataspec, rev, sha, collected, ("race_key", Race(id)), ("happyo_ts", Happyo(time)));

    private static List<DbRow> Dm(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_DM_INFO();
        parsed.SetDataB(ref text);
        var row = Start("mining_time", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.DMInfo)
        {
            SlotUmaban(
                rows,
                keys,
                "mining_time_horse",
                item.Umaban,
                ("dm_time", item.DMTime),
                ("gosa_p", item.DMGosaP),
                ("gosa_m", item.DMGosaM));
        }

        return rows;
    }

    private static List<DbRow> Tm(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_TM_INFO();
        parsed.SetDataB(ref text);
        var row = Start("mining_match", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.TMInfo)
        {
            SlotUmaban(rows, keys, "mining_match_horse", item.Umaban, ("score", item.TMScore));
        }

        return rows;
    }

    private static List<DbRow> Wh(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_WH_BATAIJYU();
        parsed.SetDataB(ref text);
        var row = Start(
            "weight",
            parsed.head,
            dataspec,
            rev,
            sha,
            collected,
            ("race_key", Race(parsed.id)),
            ("happyo_ts", Happyo(parsed.HappyoTime)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.BataijyuInfo)
        {
            if (FieldText.Blank(item.Umaban, item.Bamei, item.BaTaijyu, item.ZogenFugo, item.ZogenSa))
            {
                continue;
            }

            var number = FieldText.SmallOrNull(item.Umaban);
            if (number is null)
            {
                continue;
            }

            var child = Child(keys, "weight_horse");
            child.Add("umaban", number.Value);
            child.Add("bamei", FieldText.NameOrNull(item.Bamei));
            child.Add("bataiju", FieldText.IntOrNull(item.BaTaijyu));
            child.Add("zogen_fugo", FieldText.CodeOrNull(item.ZogenFugo));
            child.Add("zogen_sa", FieldText.IntOrNull(item.ZogenSa));
            rows.Add(child);
        }

        return rows;
    }

    private static List<DbRow> Tc(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_TC_INFO();
        parsed.SetDataB(ref text);
        var row = Start("post_time_change", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        row.Add("after_time", FieldText.CodeOrNull(parsed.TCInfoAfter.Ji + parsed.TCInfoAfter.Fun));
        row.Add("before_time", FieldText.CodeOrNull(parsed.TCInfoBefore.Ji + parsed.TCInfoBefore.Fun));
        return [row];
    }

    private static List<DbRow> Cc(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_CC_INFO();
        parsed.SetDataB(ref text);
        var row = Start("course_change", parsed.head, dataspec, rev, sha, collected, ("race_key", Race(parsed.id)));
        row.Add("after_kyori", FieldText.IntOrNull(parsed.CCInfoAfter.Kyori));
        row.Add("after_track", FieldText.CodeOrNull(parsed.CCInfoAfter.TruckCd));
        row.Add("before_kyori", FieldText.IntOrNull(parsed.CCInfoBefore.Kyori));
        row.Add("before_track", FieldText.CodeOrNull(parsed.CCInfoBefore.TruckCd));
        return [row];
    }

    private static List<DbRow> Av(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_AV_INFO();
        parsed.SetDataB(ref text);
        var row = Start(
            "entry_cancel",
            parsed.head,
            dataspec,
            rev,
            sha,
            collected,
            ("race_key", Race(parsed.id)),
            ("umaban", FieldText.KeySmall(parsed.Umaban)));
        row.Add("bamei", FieldText.NameOrNull(parsed.Bamei));
        row.Add("jiyu", FieldText.CodeOrNull(parsed.JiyuKubun));
        return [row];
    }

    private static List<DbRow> Jc(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_JC_INFO();
        parsed.SetDataB(ref text);
        var row = Start(
            "jockey_change",
            parsed.head,
            dataspec,
            rev,
            sha,
            collected,
            ("race_key", Race(parsed.id)),
            ("umaban", FieldText.KeySmall(parsed.Umaban)));
        row.Add("bamei", FieldText.NameOrNull(parsed.Bamei));
        row.Add("after_kisyu", FieldText.CodeOrNull(parsed.JCInfoAfter.KisyuCode));
        row.Add("before_kisyu", FieldText.CodeOrNull(parsed.JCInfoBefore.KisyuCode));
        return [row];
    }

    private static List<DbRow> Ys(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_YS_SCHEDULE();
        parsed.SetDataB(ref text);
        var row = Start("schedule", parsed.head, dataspec, rev, sha, collected, ("meeting_key", Meeting(parsed.id)));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        foreach (var item in parsed.JyusyoInfo)
        {
            var number = FieldText.CodeOrNull(item.TokuNum);
            if (number is null)
            {
                continue;
            }

            var child = Child(keys, "schedule_grade");
            child.Add("toku_num", number);
            child.Add("hondai", FieldText.NameOrNull(item.Hondai));
            rows.Add(child);
        }

        return rows;
    }

    private static List<DbRow> We(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_WE_WEATHER();
        parsed.SetDataB(ref text);
        var row = Start("weather", parsed.head, dataspec, rev, sha, collected, ("meeting_key", Meeting(parsed.id)));
        row.Add("tenko", FieldText.CodeOrNull(parsed.TenkoBaba.TenkoCD));
        row.Add("siba", FieldText.CodeOrNull(parsed.TenkoBaba.SibaBabaCD));
        row.Add("dirt", FieldText.CodeOrNull(parsed.TenkoBaba.DirtBabaCD));
        return [row];
    }

    private static List<DbRow> Wf(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_WF_INFO();
        parsed.SetDataB(ref text);
        var row = Start(
            "win5",
            parsed.head,
            dataspec,
            rev,
            sha,
            collected,
            ("kaisai_date", FieldText.KeyDate(Ymd(parsed.KaisaiDate))));
        var keys = KeysOf(row);
        var rows = new List<DbRow> { row };
        for (var i = 0; i < parsed.WFRaceInfo.Length; i++)
        {
            var item = parsed.WFRaceInfo[i];
            if (FieldText.Blank(item.JyoCD, item.Kaiji, item.Nichiji, item.RaceNum))
            {
                continue;
            }

            var child = Child(keys, "win5_race");
            child.Add("ord", (short)(i + 1));
            child.Add("jyo_cd", FieldText.KeyText(item.JyoCD));
            child.Add("kaiji", FieldText.KeyText(item.Kaiji));
            child.Add("nichiji", FieldText.KeyText(item.Nichiji));
            child.Add("race_num", FieldText.KeyText(item.RaceNum));
            rows.Add(child);
        }

        for (var i = 0; i < parsed.WFPayInfo.Length; i++)
        {
            var item = parsed.WFPayInfo[i];
            if (FieldText.Blank(item.Kumiban, item.Pay, item.Tekichu_Hyo))
            {
                continue;
            }

            var key = FieldText.TrimEndSpaces(item.Kumiban);
            if (key.Length == 0)
            {
                continue;
            }

            var child = Child(keys, "win5_payout");
            child.Add("ord", (short)(i + 1));
            child.Add("kumi", key);
            child.Add("pay", FieldText.LongOrNull(item.Pay));
            rows.Add(child);
        }

        return rows;
    }

    private static List<DbRow> Um(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_UM_UMA();
        parsed.SetDataB(ref text);
        var row = Start("horse", parsed.head, dataspec, rev, sha, collected, ("ketto_num", FieldText.KeyText(parsed.KettoNum)));
        var keys = KeysOf(row);
        row.Add("bamei", FieldText.NameOrNull(parsed.Bamei));
        row.Add("breeder_code", FieldText.CodeOrNull(parsed.BreederCode));
        row.Add("breeder_name", FieldText.NameOrNull(parsed.BreederName));
        var rows = new List<DbRow> { row };
        for (var i = 0; i < parsed.Ketto3Info.Length; i++)
        {
            var item = parsed.Ketto3Info[i];
            if (FieldText.Blank(item.HansyokuNum, item.Bamei))
            {
                continue;
            }

            var child = Child(keys, "horse_pedigree");
            child.Add("waku", (short)(i + 1));
            child.Add("hansyoku_num", FieldText.CodeOrNull(item.HansyokuNum));
            child.Add("bamei", FieldText.NameOrNull(item.Bamei));
            rows.Add(child);
        }

        Finish(rows, keys, "sogo", 0, parsed.ChakuSogo);
        Finish(rows, keys, "chuo", 0, parsed.ChakuChuo);
        for (var i = 0; i < parsed.ChakuKaisuBa.Length; i++)
        {
            Finish(rows, keys, "ba", (short)(i + 1), parsed.ChakuKaisuBa[i]);
        }

        for (var i = 0; i < parsed.ChakuKaisuJyotai.Length; i++)
        {
            Finish(rows, keys, "jyotai", (short)(i + 1), parsed.ChakuKaisuJyotai[i]);
        }

        for (var i = 0; i < parsed.ChakuKaisuKyori.Length; i++)
        {
            Finish(rows, keys, "kyori", (short)(i + 1), parsed.ChakuKaisuKyori[i]);
        }

        return rows;
    }

    private static void Finish(
        List<DbRow> rows,
        List<(string Name, object Value)> keys,
        string kubun,
        short waku,
        JVData_Struct.CHAKUKAISU3_INFO info)
    {
        if (info.ChakuKaisu is null || FieldText.Blank(info.ChakuKaisu))
        {
            return;
        }

        var child = Child(keys, "horse_finish_count");
        child.Add("kubun", kubun);
        child.Add("waku", waku);
        child.Add("counts", FieldText.Ints(info.ChakuKaisu));
        rows.Add(child);
    }

    private static List<DbRow> Sk(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_SK_SANKU();
        parsed.SetDataB(ref text);
        var row = Start("offspring", parsed.head, dataspec, rev, sha, collected, ("ketto_num", FieldText.KeyText(parsed.KettoNum)));
        row.Add("breeder_code", FieldText.CodeOrNull(parsed.BreederCode));
        var numbers = new string[parsed.HansyokuNum.Length];
        for (var i = 0; i < numbers.Length; i++)
        {
            numbers[i] = FieldText.CodeOrNull(parsed.HansyokuNum[i]);
        }

        row.Add("hansyoku_nums", numbers);
        return [row];
    }

    private static List<DbRow> Hy(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_HY_BAMEIORIGIN();
        parsed.SetDataB(ref text);
        var row = Start("name_origin", parsed.head, dataspec, rev, sha, collected, ("ketto_num", FieldText.KeyText(parsed.KettoNum)));
        row.Add("bamei", FieldText.NameOrNull(parsed.Bamei));
        row.Add("origin", FieldText.NameOrNull(parsed.Origin));
        return [row];
    }

    private static List<DbRow> Ks(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_KS_KISYU();
        parsed.SetDataB(ref text);
        var row = Start("jockey", parsed.head, dataspec, rev, sha, collected, ("kisyu_code", FieldText.KeyText(parsed.KisyuCode)));
        row.Add("kisyu_name", FieldText.NameOrNull(parsed.KisyuName));
        return [row];
    }

    private static List<DbRow> Ch(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_CH_CHOKYOSI();
        parsed.SetDataB(ref text);
        var row = Start("trainer", parsed.head, dataspec, rev, sha, collected, ("chokyosi_code", FieldText.KeyText(parsed.ChokyosiCode)));
        row.Add("chokyosi_name", FieldText.NameOrNull(parsed.ChokyosiName));
        return [row];
    }

    private static List<DbRow> Br(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_BR_BREEDER();
        parsed.SetDataB(ref text);
        var row = Start("breeder", parsed.head, dataspec, rev, sha, collected, ("breeder_code", FieldText.KeyText(parsed.BreederCode)));
        row.Add("breeder_name", FieldText.NameOrNull(parsed.BreederName));
        row.Add("breeder_name_co", FieldText.NameOrNull(parsed.BreederName_Co));
        row.Add("breeder_name_kana", FieldText.NameOrNull(parsed.BreederNameKana));
        return [row];
    }

    private static List<DbRow> Bn(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_BN_BANUSI();
        parsed.SetDataB(ref text);
        var row = Start("owner", parsed.head, dataspec, rev, sha, collected, ("banusi_code", FieldText.KeyText(parsed.BanusiCode)));
        row.Add("banusi_name", FieldText.NameOrNull(parsed.BanusiName));
        return [row];
    }

    private static List<DbRow> Hn(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_HN_HANSYOKU();
        parsed.SetDataB(ref text);
        var row = Start("breeding_horse", parsed.head, dataspec, rev, sha, collected, ("hansyoku_num", FieldText.KeyText(parsed.HansyokuNum)));
        row.Add("bamei", FieldText.NameOrNull(parsed.Bamei));
        row.Add("sire_num", FieldText.CodeOrNull(parsed.HansyokuFNum));
        row.Add("dam_num", FieldText.CodeOrNull(parsed.HansyokuMNum));
        return [row];
    }

    private static List<DbRow> Hc(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_HC_HANRO();
        parsed.SetDataB(ref text);
        return [Training("slope_training", parsed.head, parsed.TresenKubun, parsed.ChokyoDate, parsed.ChokyoTime, parsed.KettoNum, dataspec, rev, sha, collected)];
    }

    private static List<DbRow> Wc(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_WC_WOOD();
        parsed.SetDataB(ref text);
        return [Training("wood_training", parsed.head, parsed.TresenKubun, parsed.ChokyoDate, parsed.ChokyoTime, parsed.KettoNum, dataspec, rev, sha, collected)];
    }

    private static DbRow Training(
        string table,
        JVData_Struct.RECORD_ID head,
        string tresen,
        JVData_Struct.YMD date,
        string time,
        string ketto,
        string dataspec,
        short rev,
        byte[] sha,
        DateTimeOffset collected) =>
        Start(
            table,
            head,
            dataspec,
            rev,
            sha,
            collected,
            ("tresen", FieldText.KeyText(tresen)),
            ("chokyo_date", FieldText.KeyDate(Ymd(date))),
            ("chokyo_time", FieldText.KeyText(time)),
            ("ketto_num", FieldText.KeyText(ketto)));

    private static List<DbRow> Hs(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_HS_SALE();
        parsed.SetDataB(ref text);
        var row = Start(
            "market_price",
            parsed.head,
            dataspec,
            rev,
            sha,
            collected,
            ("ketto_num", FieldText.KeyText(parsed.KettoNum)),
            ("sale_code", FieldText.KeyText(parsed.SaleCode)),
            ("open_date", FieldText.KeyDate(Ymd(parsed.FromDate))));
        row.Add("sire_num", FieldText.CodeOrNull(parsed.HansyokuFNum));
        row.Add("dam_num", FieldText.CodeOrNull(parsed.HansyokuMNum));
        row.Add("price", FieldText.LongOrNull(parsed.Price));
        return [row];
    }

    private static List<DbRow> Cs(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_CS_COURSE();
        parsed.SetDataB(ref text);
        return
        [
            Start(
                "course",
                parsed.head,
                dataspec,
                rev,
                sha,
                collected,
                ("jyo_cd", FieldText.KeyText(parsed.JyoCD)),
                ("kyori", FieldText.KeyInt(parsed.Kyori)),
                ("track_cd", FieldText.KeyText(parsed.TrackCD)),
                ("kaishu_date", FieldText.KeyDate(Ymd(parsed.KaishuDate)))),
        ];
    }

    private static List<DbRow> Rc(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_RC_RECORD();
        parsed.SetDataB(ref text);
        return
        [
            Start(
                "race_record",
                parsed.head,
                dataspec,
                rev,
                sha,
                collected,
                ("rec_info_kubun", FieldText.KeyText(parsed.RecInfoKubun)),
                ("race_key", Race(parsed.id)),
                ("toku_num", FieldText.TrimEndSpaces(parsed.TokuNum ?? "")),
                ("syubetu_cd", FieldText.KeyText(parsed.SyubetuCD)),
                ("kyori", FieldText.KeyInt(parsed.Kyori)),
                ("track_cd", FieldText.KeyText(parsed.TrackCD))),
        ];
    }

    private static List<DbRow> Jg(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_JG_JOGAIBA();
        parsed.SetDataB(ref text);
        return
        [
            Start(
                "exclusion",
                parsed.head,
                dataspec,
                rev,
                sha,
                collected,
                ("race_key", Race(parsed.id)),
                ("ketto_num", FieldText.KeyText(parsed.KettoNum)),
                ("tohyo_jun", FieldText.KeySmall(parsed.ShutsubaTohyoJun))),
        ];
    }

    private static List<DbRow> Bt(string text, string dataspec, short rev, byte[] sha, DateTimeOffset collected)
    {
        var parsed = new JVData_Struct.JV_BT_KEITO();
        parsed.SetDataB(ref text);
        var row = Start("bloodline", parsed.head, dataspec, rev, sha, collected, ("hansyoku_num", FieldText.KeyText(parsed.HansyokuNum)));
        row.Add("keito_id", FieldText.NameOrNull(parsed.KeitoId));
        row.Add("keito_name", FieldText.NameOrNull(parsed.KeitoName));
        return [row];
    }
}
