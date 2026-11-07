# DWH ローダ データベース定義書

- 状態: ドラフト
- 日付: 2026-11-07
- 要求: [requirements.md](requirements.md)
- 構造: [architecture.md](architecture.md)
- 導入: [postgresql.md](postgresql.md)
- 入力: [../jv-data-collector/design.md](../jv-data-collector/design.md)
- 正とする仕様: JV-Data 仕様書 Ver.4.9.0.1 のフォーマットシート。推奨キーは列「キー」の `○`。`●` は時系列オッズを使うときの追加キー
- 位置づけ: スキーマ `jv` のテーブル、列、主キー、索引、有効な版のビュー。項目のバイト位置と繰り返し回数は仕様書を正とし、この文書では再定義しない

## 1. 結論

スキーマは `jv` とする。収集対象のレコード種別ごとに親テーブルを一つ置く。仕様の推奨キーを主キーの核にし、データ区分・データ作成年月日・取得元 dataspec・生バイトの SHA-256 を足して版を残す。予想が読むのは、その版から一つを選ぶビューである。

繰り返し項目は、頭や組番の並びなら子テーブルへ展開する。初期値だけの枠は行にしない。本賞金、ラップ、着回数のように並び位置が意味を持つ固定長は、親の配列列にする。

未知のレコード種別は `jv.unparsed` に種別とハッシュだけを残す。ペイロードの正本は raw のままとする。

## 2. 表の読み方

オブジェクトは次の表で管理する。種別を足すときは、テーブル一覧に 1 行足し、キーの形が新しければその表にも 1 行足す。子があるときは子テーブルの表、有効な版の選び方が既存の節と違うときはビューの表に行を足す。

親テーブルに共通な列は「共通列」に一度だけ書く。レコード種別の項目列は仕様書の並びとし、この定義書では値の型と、繰り返しを子にするか配列にするかだけを書く。

列定義の表は次の列を持つ。

| 列 | 意味 |
| --- | --- |
| 物理名 | PostgreSQL 上の名前。スキーマは `jv` |
| 意味 | その列が表すもの |
| 型 | PostgreSQL の型 |
| NULL | 不可、または可 |
| 制約 | 主キー、既定値 |

## 3. 値の置き方

SDK の C# 構造体でバイト列を切ったあと、次の規則で列へ入れる。

| 仕様の値 | 列 |
| --- | --- |
| 半角のコード | `char` または `text`。名称へは展開しない |
| 年月日 8 桁 | `date`。`00000000` と空白は NULL |
| 距離、頭数、着順、タイム、賞金、オッズ | 仕様の単位の `integer` または `numeric`。数字でない取消・無投票・発売前の記号は NULL にし、隣に理由の `text` を残す列がある項目だけ理由を残す |
| 全角・半角カナの名称 | UTF-8 の `text`。末尾の空白だけ除く |
| 全項目が `0`、半角空白、全角空白 | NULL |

文字コードは仕様どおり、全角は Shift_JIS、半角は JIS8 として読む。コード表の行はこの定義書には置かない。

2023-08-08 の桁拡張後の幅を列長にする。拡張前の値はそのまま入る。対象は変更履歴 Ver.4.9.0 が挙げる次の項目で、`UM` の 3 代血統にある繁殖登録番号も現行構造体では 10 バイトである。

| レコード | 拡張 |
| --- | --- |
| `BR` | 生産者コード 6→8。生産者名 70→72 |
| `HN` | 繁殖登録番号、父、母 8→10 |
| `SK` | 生産者コード 6→8。3 代血統の繁殖登録番号 8→10 |
| `CK` | 生産者コード 6→8。生産者名 70→72 |
| `HS` | 父・母の繁殖登録番号 8→10 |
| `BT` | 繁殖登録番号 8→10 |
| `UM` | 3 代血統の繁殖登録番号は現行 10。生産者コード 8、生産者名 72 |

レイアウトはレコード長で決める。`DIFF` `BLOD` `SNAP` `HOSE` の短いレコードは `layout_rev = 0`、`DIFN` `BLDN` `SNPN` `HOSN` と拡張後の長さは `layout_rev = 1`。同じテーブルへ書く。

## 4. 共通列

親テーブルは共通して次を持つ。

| 物理名 | 意味 | 型 | NULL | 制約 |
| --- | --- | --- | --- | --- |
| `data_kubun` | データ区分 1 桁 | `char(1)` | 不可 | 主キー |
| `make_ymd` | データ作成年月日 8 桁 | `char(8)` | 不可 | 主キー |
| `make_date` | `make_ymd` が暦日ならその日付 | `date` | 可 | |
| `src_dataspec` | 取得元。`RACE` や `0B15` | `char(4)` | 不可 | 主キー |
| `layout_rev` | 0 が拡張前、1 が拡張後 | `smallint` | 不可 | |
| `raw_sha256` | ペイロードの SHA-256 | `bytea` | 不可 | 主キー |
| `collected_at` | 台帳の取得時刻。作成年月日が同じ日の訂正を並べる | `timestamptz` | 不可 | |
| `loaded_at` | ローダが書いた時刻 | `timestamptz` | 不可 | 既定 `now()` |

主キーの順は、キーの形の列、その種別に足す列、それから `data_kubun`、`make_ymd`、`src_dataspec`、`raw_sha256` である。同じ推奨キーの訂正、区分の違い、蓄積と速報の両方を残す。データ区分 `0` は削除として残し、有効ビューからは外す。

有効行は、区分ごとに `make_ymd`、同着なら `collected_at` が新しい方を採る。作成年月日は日付だけで、同日の再送は取得時刻で決める。ビューの節で優先が書いてある種別は、その表がこの一般規則より先である。

## 5. キーの形

仕様の `○` を、次の形にまとめる。テーブル一覧の「形」列はこの名前を指す。

| 形 | 中身 |
| --- | --- |
| 競走 | `race_key char(16)`。競走識別情報 16 バイト（開催年 4、月日 4、場 2、回 2、日 2、レース番号 2） |
| 競走と馬 | `race_key` + 馬番 `smallint` + 血統登録番号 `char(10)` |
| 競走と馬番 | `race_key` + 馬番 `smallint` |
| 開催日と場回日 | `meeting_key char(14)`。競走識別情報 2 |
| 開催日 | `kaisai_date`。年月日は `date` |
| 血統登録番号 | `ketto_num char(10)` |
| コード | そのレコードのコード。物理名と桁は仕様書のその項目 |
| 調教 | トレセン区分 + 調教年月日 + 調教時刻 + 血統登録番号 |
| 取引 | 血統登録番号 + 主催者・市場コード + 開催開始日 |
| コース | 競馬場コード + 距離 + トラックコード + コース改修年月日 |
| レコード | レコード識別区分 + `race_key` + 特別競走番号 + 競走種別 + 距離 + トラックコード |
| 除外 | `race_key` + 血統登録番号 + 出馬投票受付順番 |
| 系統 | 繁殖登録番号 |

`O1` と `O2` の発表月日時分は仕様上 `●` で、時系列を使うときだけキーに入る。`O3`〜`O6` にも同じ項目があり、`0B31`〜`0B36` の周回はデータ区分 `1`（中間）のまま時刻だけが進む。`O1`〜`O6` の主キーには発表月日時分を加える。`WH` の推奨キーは競走だけだが、同日の再発表を残すため主キーに発表月日時分も入れる。有効ビューは競走あたり発表が新しい 1 行である。発表月日時分の物理名は未定で、仕様の項目に合わせてこの表へ足す。

`RC` の `○` はレース特定の項目と、競走種別・距離・トラックに分かれている。両方を主キーに含める。特別競走番号は GI レコードのときだけ意味を持つ。

`CK` の形の名前は上の表に無い。主キーの核は仕様の推奨キーとする。

## 6. テーブル

親の物理名は、中身が読める名前にする。種別 ID は一覧の「種別」に残し、物理名にはしない。子は親の名前に内容を足す。子テーブルの主キーは、親の主キーに子のキーを足したものである。

### 6.1 一覧

| 物理名 | 意味 | 種別 | 形 | 主キーに足す列 |
| --- | --- | --- | --- | --- |
| `special_entry` | 特別登録馬 | `TK` | 競走 | |
| `race` | レース詳細 | `RA` | 競走 | |
| `payout` | 払戻 | `HR` | 競走 | |
| `hyosu` | 票数（3連単以外） | `H1` | 競走 | |
| `hyosu_sanrentan` | 票数（3連単） | `H6` | 競走 | |
| `odds_tanpuku` | 単勝・複勝・枠連オッズ | `O1` | 競走 | 発表月日時分 |
| `odds_umaren` | 馬連オッズ | `O2` | 競走 | 発表月日時分 |
| `odds_wide` | ワイドオッズ | `O3` | 競走 | 発表月日時分 |
| `odds_umatan` | 馬単オッズ | `O4` | 競走 | 発表月日時分 |
| `odds_sanrenpuku` | 3連複オッズ | `O5` | 競走 | 発表月日時分 |
| `odds_sanrentan` | 3連単オッズ | `O6` | 競走 | 発表月日時分 |
| `mining_time` | タイム型マイニング | `DM` | 競走 | |
| `mining_match` | 対戦型マイニング | `TM` | 競走 | |
| `weight` | 馬体重 | `WH` | 競走 | 発表月日時分 |
| `post_time_change` | 発走時刻変更 | `TC` | 競走 | |
| `course_change` | コース変更 | `CC` | 競走 | |
| `race_horse` | 馬毎レース情報 | `SE` | 競走と馬 | |
| `entry_cancel` | 出走取消・競走除外 | `AV` | 競走と馬番 | |
| `jockey_change` | 騎手変更 | `JC` | 競走と馬番 | |
| `schedule` | 開催スケジュール | `YS` | 開催日と場回日 | |
| `weather` | 天候馬場状態 | `WE` | 開催日と場回日 | |
| `win5` | 重勝式 | `WF` | 開催日 | |
| `horse` | 競走馬マスタ | `UM` | 血統登録番号 | |
| `offspring` | 産駒マスタ | `SK` | 血統登録番号 | |
| `name_origin` | 馬名の意味由来 | `HY` | 血統登録番号 | |
| `jockey` | 騎手マスタ | `KS` | コード | |
| `trainer` | 調教師マスタ | `CH` | コード | |
| `breeder` | 生産者マスタ | `BR` | コード | |
| `owner` | 馬主マスタ | `BN` | コード | |
| `breeding_horse` | 繁殖馬マスタ | `HN` | コード | |
| `slope_training` | 坂路調教 | `HC` | 調教 | |
| `wood_training` | ウッドチップ調教 | `WC` | 調教 | |
| `market_price` | 市場取引価格 | `HS` | 取引 | |
| `course` | コース情報 | `CS` | コース | |
| `race_record` | レコードマスタ | `RC` | レコード | |
| `exclusion` | 競走馬除外 | `JG` | 除外 | |
| `bloodline` | 系統情報 | `BT` | 系統 | |
| `chakudo` | 出走別着度数 | `CK` | 仕様の推奨キー | |

### 6.2 子テーブル

| 親 | 子 | 子のキー |
| --- | --- | --- |
| `special_entry` | `special_entry_horse` | 連番。血統登録番号を列に持つ |
| `race` | `race_corner` | コーナー番号 |
| `race_horse` | `race_horse_rival` | 1 着馬から 3 頭まで。空は入れない |
| `payout` | `payout_line` | 式別 + 並び順 |
| `hyosu` | `hyosu_line` | 式別 + 組番 |
| `hyosu_sanrentan` | `hyosu_sanrentan_line` | 組番 |
| `odds_tanpuku` | `odds_tanpuku_tan` `odds_tanpuku_fuku` `odds_tanpuku_waku` | 馬番または組番 |
| `odds_umaren` | `odds_umaren_kumi` | 組番 |
| `odds_wide` | `odds_wide_kumi` | 組番 |
| `odds_umatan` | `odds_umatan_kumi` | 組番 |
| `odds_sanrenpuku` | `odds_sanrenpuku_kumi` | 組番 |
| `odds_sanrentan` | `odds_sanrentan_kumi` | 組番 |
| `horse` | `horse_pedigree` `horse_finish_count` | 血統の枠、着回数の区分と枠 |
| `mining_time` | `mining_time_horse` | 馬番 |
| `mining_match` | `mining_match_horse` | 馬番 |
| `weight` | `weight_horse` | 馬番 |
| `win5` | `win5_race` `win5_payout` | 対象順、払戻の並び |
| `schedule` | `schedule_grade` | 特別競走番号 |
| `bloodline` | `bloodline_node` | 仕様の系譜位置 |

`hyosu_sanrentan` と `odds_sanrentan` は組が多い。初期値の組を除いても、確定週は行数が大きい。親 1 行に対する子として置き、予想の最新オッズは `v_odds_sanrentan_latest` だけを読む。

### 6.3 配列列

`race` の本賞金・付加賞金・ラップタイム、`horse` と `chakudo` の着回数のうち枠が固定で位置が意味を持つものは `integer[]` または `numeric[]` とする。`chakudo` は 1 レコードが競走馬・騎手・調教師・馬主・生産者の着回数ブロックを持つ。ブロックごとに列を分け、子テーブルにはしない。

### 6.4 列の見本（`jv.race_horse`）

他の親は、形の列をこの 3 列と入れ替え、項目列をそのレコードの仕様に替える。共通列はそのままである。

主キーの順は `race_key`、`umaban`、`ketto_num`、`data_kubun`、`make_ymd`、`src_dataspec`、`raw_sha256`。

| 物理名 | 意味 | 型 | NULL | 制約 |
| --- | --- | --- | --- | --- |
| `race_key` | 競走識別情報 | `char(16)` | 不可 | 主キー |
| `umaban` | 馬番 | `smallint` | 不可 | 主キー |
| `ketto_num` | 血統登録番号 | `char(10)` | 不可 | 主キー |
| 共通列 | 版 | 共通列の節 | | `data_kubun` `make_ymd` `src_dataspec` `raw_sha256` が主キー |
| `wakuban` | 枠番 | `smallint` | 可 | |
| `bamei` | 馬名 | `text` | 可 | |
| 項目列 | 仕様書「３．馬毎レース情報」の残り | 値の置き方に従う | | |

| 物理名 | テーブル | 列 |
| --- | --- | --- |
| `race_horse_ketto` | `race_horse` | `ketto_num`, `make_date` |
| `race_horse_race` | `race_horse` | `race_key`, `data_kubun` |

`meeting_date` は `race_key` の先頭 8 桁からビュー側で date にする。開催日の範囲で読むビューは、その式にインデックスが要る段階で生成列を足す。

## 7. ビュー

ビューは親の全版から、キーごとに 1 行を選ぶ。名前を付けたビューは次のとおり。採る行は続く節の表が正である。

| ビュー | 親 |
| --- | --- |
| `v_race_program` `v_race_horse_program` | `race` `race_horse` |
| `v_race_result` `v_race_horse_result` | `race` `race_horse` |
| `v_race_horse_local` | `race_horse` |
| `v_odds_tanpuku_latest` から `v_odds_sanrentan_latest` | `odds_tanpuku` から `odds_sanrentan` |
| `v_odds_tanpuku_final` から `v_odds_sanrentan_final` | `odds_tanpuku` から `odds_sanrentan` |
| `v_hyosu_final` `v_hyosu_sanrentan_final` | `hyosu` `hyosu_sanrentan` |
| `v_payout` | `payout` |
| `v_mining_time` `v_mining_match` | `mining_time` `mining_match` |
| `v_win5` | `win5` |
| `v_special_entry` | `special_entry` |
| `v_horse` `v_trainer` `v_jockey` `v_owner` `v_breeder` | `horse` `trainer` `jockey` `owner` `breeder` |
| `v_weight` | `weight` |
| `v_jockey_change` `v_entry_cancel` `v_post_time_change` `v_weather` `v_course_change` | `jockey_change` `entry_cancel` `post_time_change` `weather` `course_change` |
| `v_schedule` | `schedule` |

### 7.1 `RA` と `SE`

データ区分は段階である。番組は `1` 出走馬名表、`2` 出馬表。成績は `3` から `6` が速報の確定段階、`7` が月曜の確定成績。`A` と `B` は地方と海外国際で、`DIFF` / `DIFN` に入る。`9` は中止、`0` は削除。

`v_race_horse_result` の粒度は `race_key`、馬番、血統登録番号の 1 行である。`v_race_result` の粒度は `race_key` の 1 行である。番組ビューも同じ粒度で、区分の採り方が次の表になる。

| ビュー | 採る行 |
| --- | --- |
| `v_race_program` `v_race_horse_program` | 区分 `2` を `1` より先。同じ区分では `0B15` を `RACE` より先。そのあと作成年月日、取得時刻 |
| `v_race_result` `v_race_horse_result` | `RACE` の区分 `7` を最優先にする。無ければ区分 `6` `5` `4` `3` の順。同じ区分では `RACE`、`0B12`、`0B15` の順。そのあと作成年月日、取得時刻 |
| `v_race_horse_local` | 区分 `A` と `B`。作成年月日、取得時刻 |

確定成績が速報より古い作成年月日でも、`RACE` の区分 `7` を有効にする。番組の変更は `0B15` が先に届くので、同じ区分では速報を蓄積より先にする。中止 `9` は、それより新しい番組区分が無いとき `v_race_program` と `v_race_horse_program` の行になる。

`SE` の推奨キーは馬番と血統登録番号を含む。番号が変わった馬は別キーとして残る。

### 7.2 オッズ、票数、払戻、マイニング

| ビュー | 採る行 |
| --- | --- |
| `v_odds_tanpuku_latest` から `v_odds_sanrentan_latest` | 区分 `1` `2` `3` のうち発表月日時分が新しいもの。予想が読む最新オッズ |
| `v_odds_tanpuku_final` から `v_odds_sanrentan_final` | 区分 `4` を `5` より先。確定オッズ |
| `v_hyosu_final` `v_hyosu_sanrentan_final` | 区分 `4` を `5` より先 |
| `v_payout` | 区分 `2` を `1` より先。月曜の確定払戻を速報より先にする |
| `v_mining_time` `v_mining_match` | 区分 `7`、`3`、`2`、`1` の順。月曜の蓄積が無い開催日は直前予想 `3` が有効になる |
| `v_win5` | 区分 `7`、`3`、`2`、`1` の順 |

`DM` と `TM` は、蓄積の区分 `7` のあとでも速報には区分 `3` が残る。ビューは蓄積の `7` を有効にする。

`v_weight` は競走あたり、発表月日時分が新しい 1 行である。

### 7.3 `0B14`

`WE` `AV` `JC` `TC` `CC` は、台帳の開催日ごとの有効世代に属するレコードだけを有効にする。ローダはその世代を `jv.ob14_active` へ写す。ビューはここを見て、世代に無い行を出さない。

`0B16` は変更の通知として先に届く。その開催日に有効な `0B14` 世代があるあいだは、世代に含まれる行だけを有効にする。世代がまだ無い開催日は、届いている `0B16` の行を有効にする。`JVRTOpen` の該当なしでは世代を進めない、は収集側の台帳に従う。

### 7.4 特別登録馬

`TK` の区分 `1`（ハンデ発表前）と区分 `2`（ハンデ発表後）は、同じ競走キーの別の版として両方残す。主キーにデータ区分を含める。`v_special_entry` は区分 `0` を除き、区分ごとに作成年月日が新しい行を返す。予想のフルデータには含めない。蓄積として残す。

### 7.5 マスタと調教

`UM` `KS` `CH` `BR` `BN` `HN` `SK` `HY` `BT` `CS` `HS` `HC` `WC` `CK` `YS` `RC` `JG` の有効行は、削除区分 `0` を除いた最新の作成年月日である。`UM` の区分 `9` は抹消として有効行に残す。`YS` は区分 `3`、`2`、`1` の順で、中止 `9` は新しい開催予定が無いとき有効にする。

この節でビュー名があるのは、予想が読む `v_horse` `v_trainer` `v_jockey` `v_owner` `v_breeder` `v_schedule` である。残りの種別も同じ規則で有効行を決める。ビューの物理名は、読む相手が決まったときに上の一覧へ足す。

## 8. 予想が読むビュー

全体要求のフルデータは、次のビューで足りる。

| フルデータの項目 | ビュー |
| --- | --- |
| 出走馬のマスタ | `v_horse`。調教師・騎手・馬主・生産者は `v_trainer` `v_jockey` `v_owner` `v_breeder` |
| 過去走 | `v_race_horse_result` と `v_race_horse_local`。対象馬の血統登録番号で引く |
| 番組 | `v_race_program` と `v_race_horse_program` |
| 確定している成績 | `v_race_horse_result` と `v_race_result` |
| 馬体重 | `v_weight` |
| 変更、出走取消、発走、天候馬場、コース | `v_jockey_change` `v_entry_cancel` `v_post_time_change` `v_weather` `v_course_change`。いずれも `ob14_active` を満たす行 |
| 出馬と開催 | `v_race_horse_program` `v_race_program` `v_schedule` |
| 速報マイニング | `v_mining_time` `v_mining_match` |
| 最新オッズ | `v_odds_tanpuku_latest` から `v_odds_sanrentan_latest` |

払戻、票数、確定オッズ、`WF`、調教、血統、市場、馬名由来は倉庫に載せる。予想の開始契機には使わない。

## 9. 取り込みの台帳

仕様のレコードとは別に、ローダが自分の進捗を置く。属性は次のとおり。物理名、型、主キーが決まったら、共通列と同じ列定義の表に書き換える。

| テーブル | 内容 |
| --- | --- |
| `jv.ingest_file` | 読み終えた完了ファイルのパス、dataspec、台帳上のファイル id、投入時刻。同じファイルを二度展開しない |
| `jv.ob14_active` | 開催日と有効世代。台帳の写し。ローダだけが書く |
| `jv.unparsed` | 未知のレコード種別、dataspec、`raw_sha256`、初回の投入時刻 |

水位と raw の索引は SQLite に残し、PostgreSQL では進めない。

## 10. 今回決めないこと

- ローダのプロセスの置き方と、完了ファイルへの追随の実装
- ローダ用ロールの権限、バックアップ。導入手順は [postgresql.md](postgresql.md)
- コード表の名称テーブル
- 予想の特徴量テーブルと結果の保存先
- 台帳 3 テーブルの物理名、型、主キー
- 発表月日時分の物理名
- `race_horse` 以外の索引。開催日で読むための生成列
