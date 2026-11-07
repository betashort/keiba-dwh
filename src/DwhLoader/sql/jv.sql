-- スーパーユーザがデータベース keiba に適用する。ローダは起動時にテーブルを作らない。
-- chakudo は主キー列名が定義されるまで親テーブルを作らず、unparsed に入る。

CREATE SCHEMA IF NOT EXISTS jv;

COMMENT ON SCHEMA jv IS 'JRA-VAN Data Lab. の JV-Data。収集アプリが残す蓄積と速報を、ローダが解釈した行。';

CREATE TABLE jv.ingest_file (
    ledger_file_id bigint PRIMARY KEY,
    dataspec char(4) NOT NULL,
    path text NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE jv.ob14_active (
    meeting_day date NOT NULL,
    generation integer NOT NULL,
    raw_sha256 bytea NOT NULL,
    record_id char(2) NOT NULL,
    PRIMARY KEY (meeting_day, raw_sha256)
);

CREATE TABLE jv.unparsed (
    record_id text NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (record_id, src_dataspec, raw_sha256)
);

CREATE TABLE jv.special_entry (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.special_entry_horse (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    seq smallint NOT NULL,
    ketto_num char(10),
    bamei text,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256, seq)
);

CREATE TABLE jv.race (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    honsyo integer[] NOT NULL,
    fuka integer[] NOT NULL,
    lap integer[] NOT NULL,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.race_corner (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    corner smallint NOT NULL,
    syukaisu text,
    jyuni text,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256, corner)
);

CREATE TABLE jv.payout (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.payout_line (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    bet text NOT NULL,
    ord smallint NOT NULL,
    kumi text NOT NULL,
    pay bigint,
    ninki integer,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256, bet, ord)
);

CREATE TABLE jv.hyosu (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.hyosu_line (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    bet text NOT NULL,
    kumi text NOT NULL,
    hyo bigint,
    ninki integer,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256, bet, kumi)
);

CREATE TABLE jv.hyosu_sanrentan (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.hyosu_sanrentan_line (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kumi text NOT NULL,
    hyo bigint,
    ninki integer,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256, kumi)
);

CREATE TABLE jv.odds_tanpuku (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.odds_tanpuku_tan (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    umaban smallint NOT NULL,
    odds integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, umaban)
);

CREATE TABLE jv.odds_tanpuku_fuku (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    umaban smallint NOT NULL,
    odds_low integer,
    odds_high integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, umaban)
);

CREATE TABLE jv.odds_tanpuku_waku (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kumi text NOT NULL,
    odds integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, kumi)
);

CREATE TABLE jv.odds_umaren (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.odds_umaren_kumi (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kumi text NOT NULL,
    odds integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, kumi)
);

CREATE TABLE jv.odds_wide (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.odds_wide_kumi (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kumi text NOT NULL,
    odds_low integer,
    odds_high integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, kumi)
);

CREATE TABLE jv.odds_umatan (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.odds_umatan_kumi (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kumi text NOT NULL,
    odds integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, kumi)
);

CREATE TABLE jv.odds_sanrenpuku (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.odds_sanrenpuku_kumi (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kumi text NOT NULL,
    odds integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, kumi)
);

CREATE TABLE jv.odds_sanrentan (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.odds_sanrentan_kumi (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kumi text NOT NULL,
    odds integer,
    ninki integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, kumi)
);

CREATE TABLE jv.mining_time (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.mining_time_horse (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    umaban smallint NOT NULL,
    dm_time integer,
    gosa_p integer,
    gosa_m integer,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256, umaban)
);

CREATE TABLE jv.mining_match (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.mining_match_horse (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    umaban smallint NOT NULL,
    score integer,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256, umaban)
);

CREATE TABLE jv.weight (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.weight_horse (
    race_key char(16) NOT NULL,
    happyo_ts char(12) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    umaban smallint NOT NULL,
    bamei text,
    bataiju integer,
    zogen_fugo text,
    zogen_sa integer,
    PRIMARY KEY (race_key, happyo_ts, data_kubun, make_ymd, src_dataspec, raw_sha256, umaban)
);

CREATE TABLE jv.post_time_change (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    after_time text,
    before_time text,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.course_change (
    race_key char(16) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    after_kyori integer,
    after_track text,
    before_kyori integer,
    before_track text,
    PRIMARY KEY (race_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.race_horse (
    race_key char(16) NOT NULL,
    umaban smallint NOT NULL,
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    wakuban smallint,
    bamei text,
    PRIMARY KEY (race_key, umaban, ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE INDEX race_horse_ketto ON jv.race_horse (ketto_num, make_date);

CREATE INDEX race_horse_race ON jv.race_horse (race_key, data_kubun);

CREATE TABLE jv.race_horse_rival (
    race_key char(16) NOT NULL,
    umaban smallint NOT NULL,
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    ord smallint NOT NULL,
    rival_ketto char(10),
    bamei text,
    PRIMARY KEY (race_key, umaban, ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256, ord)
);

CREATE TABLE jv.entry_cancel (
    race_key char(16) NOT NULL,
    umaban smallint NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    bamei text,
    jiyu text,
    PRIMARY KEY (race_key, umaban, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.jockey_change (
    race_key char(16) NOT NULL,
    umaban smallint NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    bamei text,
    after_kisyu text,
    before_kisyu text,
    PRIMARY KEY (race_key, umaban, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.schedule (
    meeting_key char(14) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (meeting_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.schedule_grade (
    meeting_key char(14) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    toku_num text NOT NULL,
    hondai text,
    PRIMARY KEY (meeting_key, data_kubun, make_ymd, src_dataspec, raw_sha256, toku_num)
);

CREATE TABLE jv.weather (
    meeting_key char(14) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    tenko text,
    siba text,
    dirt text,
    PRIMARY KEY (meeting_key, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.win5 (
    kaisai_date date NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (kaisai_date, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.win5_race (
    kaisai_date date NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    ord smallint NOT NULL,
    jyo_cd char(2) NOT NULL,
    kaiji char(2) NOT NULL,
    nichiji char(2) NOT NULL,
    race_num char(2) NOT NULL,
    PRIMARY KEY (kaisai_date, data_kubun, make_ymd, src_dataspec, raw_sha256, ord)
);

CREATE TABLE jv.win5_payout (
    kaisai_date date NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    ord smallint NOT NULL,
    kumi text NOT NULL,
    pay bigint,
    PRIMARY KEY (kaisai_date, data_kubun, make_ymd, src_dataspec, raw_sha256, ord)
);

CREATE TABLE jv.horse (
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    bamei text,
    breeder_code char(8),
    breeder_name text,
    PRIMARY KEY (ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.horse_pedigree (
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    waku smallint NOT NULL,
    hansyoku_num char(10),
    bamei text,
    PRIMARY KEY (ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256, waku)
);

CREATE TABLE jv.horse_finish_count (
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    kubun text NOT NULL,
    waku smallint NOT NULL,
    counts integer[] NOT NULL,
    PRIMARY KEY (ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256, kubun, waku)
);

CREATE TABLE jv.offspring (
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    breeder_code char(8),
    hansyoku_nums char(10)[],
    PRIMARY KEY (ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.name_origin (
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    bamei text,
    origin text,
    PRIMARY KEY (ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.jockey (
    kisyu_code char(5) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    kisyu_name text,
    PRIMARY KEY (kisyu_code, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.trainer (
    chokyosi_code char(5) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    chokyosi_name text,
    PRIMARY KEY (chokyosi_code, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.breeder (
    breeder_code char(8) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    breeder_name text,
    breeder_name_co text,
    breeder_name_kana text,
    PRIMARY KEY (breeder_code, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.owner (
    banusi_code char(6) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    banusi_name text,
    PRIMARY KEY (banusi_code, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.breeding_horse (
    hansyoku_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    bamei text,
    sire_num char(10),
    dam_num char(10),
    PRIMARY KEY (hansyoku_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.slope_training (
    tresen char(1) NOT NULL,
    chokyo_date date NOT NULL,
    chokyo_time char(4) NOT NULL,
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tresen, chokyo_date, chokyo_time, ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.wood_training (
    tresen char(1) NOT NULL,
    chokyo_date date NOT NULL,
    chokyo_time char(4) NOT NULL,
    ketto_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tresen, chokyo_date, chokyo_time, ketto_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.market_price (
    ketto_num char(10) NOT NULL,
    sale_code char(6) NOT NULL,
    open_date date NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    sire_num char(10),
    dam_num char(10),
    price bigint,
    PRIMARY KEY (ketto_num, sale_code, open_date, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.course (
    jyo_cd char(2) NOT NULL,
    kyori integer NOT NULL,
    track_cd char(2) NOT NULL,
    kaishu_date date NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (jyo_cd, kyori, track_cd, kaishu_date, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.race_record (
    rec_info_kubun char(1) NOT NULL,
    race_key char(16) NOT NULL,
    toku_num text NOT NULL,
    syubetu_cd char(2) NOT NULL,
    kyori integer NOT NULL,
    track_cd char(2) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (rec_info_kubun, race_key, toku_num, syubetu_cd, kyori, track_cd, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.exclusion (
    race_key char(16) NOT NULL,
    ketto_num char(10) NOT NULL,
    tohyo_jun smallint NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (race_key, ketto_num, tohyo_jun, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.bloodline (
    hansyoku_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    make_date date,
    src_dataspec char(4) NOT NULL,
    layout_rev smallint NOT NULL,
    raw_sha256 bytea NOT NULL,
    collected_at timestamptz NOT NULL,
    loaded_at timestamptz NOT NULL DEFAULT now(),
    keito_id text,
    keito_name text,
    PRIMARY KEY (hansyoku_num, data_kubun, make_ymd, src_dataspec, raw_sha256)
);

CREATE TABLE jv.bloodline_node (
    hansyoku_num char(10) NOT NULL,
    data_kubun char(1) NOT NULL,
    make_ymd char(8) NOT NULL,
    src_dataspec char(4) NOT NULL,
    raw_sha256 bytea NOT NULL,
    pos smallint NOT NULL,
    PRIMARY KEY (hansyoku_num, data_kubun, make_ymd, src_dataspec, raw_sha256, pos)
);

CREATE VIEW jv.v_race_program AS
SELECT *
FROM (
    SELECT DISTINCT ON (race_key) r.*
    FROM jv.race r
    WHERE r.data_kubun IN ('1', '2')
    ORDER BY r.race_key,
        CASE r.data_kubun WHEN '2' THEN 0 ELSE 1 END,
        CASE btrim(r.src_dataspec) WHEN '0B15' THEN 0 WHEN 'RACE' THEN 1 ELSE 2 END,
        r.make_ymd DESC,
        r.collected_at DESC
) prog
WHERE NOT EXISTS (
    SELECT 1 FROM jv.race c
    WHERE c.race_key = prog.race_key
      AND c.data_kubun = '9'
      AND c.make_ymd > prog.make_ymd
)
UNION ALL
SELECT c.*
FROM (
    SELECT DISTINCT ON (race_key) *
    FROM jv.race
    WHERE data_kubun = '9'
    ORDER BY race_key, make_ymd DESC, collected_at DESC
) c
WHERE NOT EXISTS (
    SELECT 1 FROM jv.race p
    WHERE p.race_key = c.race_key
      AND p.data_kubun IN ('1', '2')
      AND p.make_ymd >= c.make_ymd
);

CREATE VIEW jv.v_race_horse_program AS
SELECT *
FROM (
    SELECT DISTINCT ON (race_key, umaban, ketto_num) r.*
    FROM jv.race_horse r
    WHERE r.data_kubun IN ('1', '2')
    ORDER BY r.race_key, r.umaban, r.ketto_num,
        CASE r.data_kubun WHEN '2' THEN 0 ELSE 1 END,
        CASE btrim(r.src_dataspec) WHEN '0B15' THEN 0 WHEN 'RACE' THEN 1 ELSE 2 END,
        r.make_ymd DESC,
        r.collected_at DESC
) prog
WHERE NOT EXISTS (
    SELECT 1 FROM jv.race_horse c
    WHERE c.race_key = prog.race_key
      AND c.umaban = prog.umaban
      AND c.ketto_num = prog.ketto_num
      AND c.data_kubun = '9'
      AND c.make_ymd > prog.make_ymd
)
UNION ALL
SELECT c.*
FROM (
    SELECT DISTINCT ON (race_key, umaban, ketto_num) *
    FROM jv.race_horse
    WHERE data_kubun = '9'
    ORDER BY race_key, umaban, ketto_num, make_ymd DESC, collected_at DESC
) c
WHERE NOT EXISTS (
    SELECT 1 FROM jv.race_horse p
    WHERE p.race_key = c.race_key
      AND p.umaban = c.umaban
      AND p.ketto_num = c.ketto_num
      AND p.data_kubun IN ('1', '2')
      AND p.make_ymd >= c.make_ymd
);

CREATE VIEW jv.v_race_result AS
SELECT DISTINCT ON (race_key) *
FROM jv.race
WHERE data_kubun IN ('7', '6', '5', '4', '3')
ORDER BY race_key,
    CASE data_kubun WHEN '7' THEN 0 WHEN '6' THEN 1 WHEN '5' THEN 2 WHEN '4' THEN 3 WHEN '3' THEN 4 ELSE 9 END,
    CASE btrim(src_dataspec) WHEN 'RACE' THEN 0 WHEN '0B12' THEN 1 WHEN '0B15' THEN 2 ELSE 3 END,
    make_ymd DESC,
    collected_at DESC;

CREATE VIEW jv.v_race_horse_result AS
SELECT DISTINCT ON (race_key, umaban, ketto_num) *
FROM jv.race_horse
WHERE data_kubun IN ('7', '6', '5', '4', '3')
ORDER BY race_key, umaban, ketto_num,
    CASE data_kubun WHEN '7' THEN 0 WHEN '6' THEN 1 WHEN '5' THEN 2 WHEN '4' THEN 3 WHEN '3' THEN 4 ELSE 9 END,
    CASE btrim(src_dataspec) WHEN 'RACE' THEN 0 WHEN '0B12' THEN 1 WHEN '0B15' THEN 2 ELSE 3 END,
    make_ymd DESC,
    collected_at DESC;

CREATE VIEW jv.v_race_horse_local AS
SELECT DISTINCT ON (race_key, umaban, ketto_num) *
FROM jv.race_horse
WHERE data_kubun IN ('A', 'B')
ORDER BY race_key, umaban, ketto_num, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_tanpuku_latest AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_tanpuku
WHERE data_kubun IN ('1', '2', '3')
ORDER BY race_key, happyo_ts DESC, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_umaren_latest AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_umaren
WHERE data_kubun IN ('1', '2', '3')
ORDER BY race_key, happyo_ts DESC, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_wide_latest AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_wide
WHERE data_kubun IN ('1', '2', '3')
ORDER BY race_key, happyo_ts DESC, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_umatan_latest AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_umatan
WHERE data_kubun IN ('1', '2', '3')
ORDER BY race_key, happyo_ts DESC, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_sanrenpuku_latest AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_sanrenpuku
WHERE data_kubun IN ('1', '2', '3')
ORDER BY race_key, happyo_ts DESC, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_sanrentan_latest AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_sanrentan
WHERE data_kubun IN ('1', '2', '3')
ORDER BY race_key, happyo_ts DESC, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_tanpuku_final AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_tanpuku
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_umaren_final AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_umaren
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_wide_final AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_wide
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_umatan_final AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_umatan
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_sanrenpuku_final AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_sanrenpuku
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_odds_sanrentan_final AS
SELECT DISTINCT ON (race_key) * FROM jv.odds_sanrentan
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_hyosu_final AS
SELECT DISTINCT ON (race_key) * FROM jv.hyosu
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_hyosu_sanrentan_final AS
SELECT DISTINCT ON (race_key) * FROM jv.hyosu_sanrentan
WHERE data_kubun IN ('4', '5')
ORDER BY race_key, CASE data_kubun WHEN '4' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_payout AS
SELECT DISTINCT ON (race_key) * FROM jv.payout
WHERE data_kubun IN ('1', '2')
ORDER BY race_key, CASE data_kubun WHEN '2' THEN 0 ELSE 1 END, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_mining_time AS
SELECT DISTINCT ON (race_key) * FROM jv.mining_time
WHERE data_kubun IN ('7', '3', '2', '1')
ORDER BY race_key,
    CASE data_kubun WHEN '7' THEN 0 WHEN '3' THEN 1 WHEN '2' THEN 2 WHEN '1' THEN 3 ELSE 9 END,
    make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_mining_match AS
SELECT DISTINCT ON (race_key) * FROM jv.mining_match
WHERE data_kubun IN ('7', '3', '2', '1')
ORDER BY race_key,
    CASE data_kubun WHEN '7' THEN 0 WHEN '3' THEN 1 WHEN '2' THEN 2 WHEN '1' THEN 3 ELSE 9 END,
    make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_win5 AS
SELECT DISTINCT ON (kaisai_date) * FROM jv.win5
WHERE data_kubun IN ('7', '3', '2', '1')
ORDER BY kaisai_date,
    CASE data_kubun WHEN '7' THEN 0 WHEN '3' THEN 1 WHEN '2' THEN 2 WHEN '1' THEN 3 ELSE 9 END,
    make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_special_entry AS
SELECT DISTINCT ON (race_key, data_kubun) * FROM jv.special_entry
WHERE data_kubun <> '0'
ORDER BY race_key, data_kubun, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_horse AS
SELECT DISTINCT ON (ketto_num) * FROM jv.horse
WHERE data_kubun <> '0'
ORDER BY ketto_num, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_trainer AS
SELECT DISTINCT ON (chokyosi_code) * FROM jv.trainer
WHERE data_kubun <> '0'
ORDER BY chokyosi_code, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_jockey AS
SELECT DISTINCT ON (kisyu_code) * FROM jv.jockey
WHERE data_kubun <> '0'
ORDER BY kisyu_code, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_owner AS
SELECT DISTINCT ON (banusi_code) * FROM jv.owner
WHERE data_kubun <> '0'
ORDER BY banusi_code, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_breeder AS
SELECT DISTINCT ON (breeder_code) * FROM jv.breeder
WHERE data_kubun <> '0'
ORDER BY breeder_code, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_weight AS
SELECT DISTINCT ON (race_key) * FROM jv.weight
WHERE data_kubun <> '0'
ORDER BY race_key, happyo_ts DESC, make_ymd DESC, collected_at DESC;

CREATE VIEW jv.v_schedule AS
SELECT *
FROM (
    SELECT DISTINCT ON (meeting_key) s.*
    FROM jv.schedule s
    WHERE s.data_kubun IN ('1', '2', '3')
    ORDER BY s.meeting_key,
        CASE s.data_kubun WHEN '3' THEN 0 WHEN '2' THEN 1 ELSE 2 END,
        s.make_ymd DESC,
        s.collected_at DESC
) plan
WHERE NOT EXISTS (
    SELECT 1 FROM jv.schedule c
    WHERE c.meeting_key = plan.meeting_key
      AND c.data_kubun = '9'
      AND c.make_ymd > plan.make_ymd
)
UNION ALL
SELECT c.*
FROM (
    SELECT DISTINCT ON (meeting_key) *
    FROM jv.schedule
    WHERE data_kubun = '9'
    ORDER BY meeting_key, make_ymd DESC, collected_at DESC
) c
WHERE NOT EXISTS (
    SELECT 1 FROM jv.schedule p
    WHERE p.meeting_key = c.meeting_key
      AND p.data_kubun IN ('1', '2', '3')
      AND p.make_ymd >= c.make_ymd
);

CREATE VIEW jv.v_weather AS
SELECT DISTINCT ON (w.meeting_key) w.*
FROM jv.weather w
WHERE w.data_kubun <> '0'
  AND (
    EXISTS (
        SELECT 1 FROM jv.ob14_active a
        WHERE a.meeting_day = to_date(substring(w.meeting_key from 1 for 8), 'YYYYMMDD')
          AND a.raw_sha256 = w.raw_sha256
          AND a.record_id = 'WE'
    )
    OR (
        NOT EXISTS (
            SELECT 1 FROM jv.ob14_active a
            WHERE a.meeting_day = to_date(substring(w.meeting_key from 1 for 8), 'YYYYMMDD')
        )
        AND btrim(w.src_dataspec) = '0B16'
    )
  )
ORDER BY w.meeting_key, w.make_ymd DESC, w.collected_at DESC;

CREATE VIEW jv.v_entry_cancel AS
SELECT DISTINCT ON (w.race_key, w.umaban) w.*
FROM jv.entry_cancel w
WHERE w.data_kubun <> '0'
  AND (
    EXISTS (
        SELECT 1 FROM jv.ob14_active a
        WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
          AND a.raw_sha256 = w.raw_sha256
          AND a.record_id = 'AV'
    )
    OR (
        NOT EXISTS (
            SELECT 1 FROM jv.ob14_active a
            WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
        )
        AND btrim(w.src_dataspec) = '0B16'
    )
  )
ORDER BY w.race_key, w.umaban, w.make_ymd DESC, w.collected_at DESC;

CREATE VIEW jv.v_jockey_change AS
SELECT DISTINCT ON (w.race_key, w.umaban) w.*
FROM jv.jockey_change w
WHERE w.data_kubun <> '0'
  AND (
    EXISTS (
        SELECT 1 FROM jv.ob14_active a
        WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
          AND a.raw_sha256 = w.raw_sha256
          AND a.record_id = 'JC'
    )
    OR (
        NOT EXISTS (
            SELECT 1 FROM jv.ob14_active a
            WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
        )
        AND btrim(w.src_dataspec) = '0B16'
    )
  )
ORDER BY w.race_key, w.umaban, w.make_ymd DESC, w.collected_at DESC;

CREATE VIEW jv.v_post_time_change AS
SELECT DISTINCT ON (w.race_key) w.*
FROM jv.post_time_change w
WHERE w.data_kubun <> '0'
  AND (
    EXISTS (
        SELECT 1 FROM jv.ob14_active a
        WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
          AND a.raw_sha256 = w.raw_sha256
          AND a.record_id = 'TC'
    )
    OR (
        NOT EXISTS (
            SELECT 1 FROM jv.ob14_active a
            WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
        )
        AND btrim(w.src_dataspec) = '0B16'
    )
  )
ORDER BY w.race_key, w.make_ymd DESC, w.collected_at DESC;

CREATE VIEW jv.v_course_change AS
SELECT DISTINCT ON (w.race_key) w.*
FROM jv.course_change w
WHERE w.data_kubun <> '0'
  AND (
    EXISTS (
        SELECT 1 FROM jv.ob14_active a
        WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
          AND a.raw_sha256 = w.raw_sha256
          AND a.record_id = 'CC'
    )
    OR (
        NOT EXISTS (
            SELECT 1 FROM jv.ob14_active a
            WHERE a.meeting_day = to_date(substring(w.race_key from 1 for 8), 'YYYYMMDD')
        )
        AND btrim(w.src_dataspec) = '0B16'
    )
  )
ORDER BY w.race_key, w.make_ymd DESC, w.collected_at DESC;
