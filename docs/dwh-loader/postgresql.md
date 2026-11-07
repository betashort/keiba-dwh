# PostgreSQL

- 状態: ドラフト
- 日付: 2026-11-07
- 要求: [requirements.md](requirements.md)
- 構造: [architecture.md](architecture.md)
- 設計: [design.md](design.md)
- データベース定義書: [schema.md](schema.md)
- 位置づけ: この PC に入っている PostgreSQL 16 を倉庫のサーバにする。データベース `keiba` とスキーマ `jv` をそのサーバに作る。テーブル、主キー、ビューは [schema.md](schema.md)。ローダ用ロールの権限は [design.md](design.md)。バックアップは未決。レコードのバイト定義は公式仕様書を正とし、ここでは再定義しない。

## 1. 結論

倉庫のサーバは、この PC に入っている PostgreSQL 16.4 である。インストール先は `C:\Program Files\PostgreSQL\16`、データディレクトリは `C:\Program Files\PostgreSQL\16\data` である。16 のコミュニティサポートは 2028-11-09 までである。

サーバは Windows サービス `postgresql-x64-16` として常駐する。スタートアップは自動、サービスアカウントは `NT AUTHORITY\NetworkService` である。ポートは `5432` である。データベースのスーパーユーザは `postgres` である。Windows のローカルユーザ `postgres` は無い。

待ち受けは `listen_addresses = '*'` で、`5432` はすべてのインターフェースで開いている。`pg_hba.conf` の `host` 行は `127.0.0.1/32` と `::1/128` だけで、認証は `scram-sha-256` である。ログインできるのはこの PC のループバックだけである。

クラスタのロケールは `Japanese_Japan.932`、タイムゾーンは `Asia/Tokyo` である。データベース `keiba` は `TEMPLATE template0`、エンコーディング `UTF8`、ロケール `C` で作る。クラスタの既定のまま作るとエンコーディングが `WIN932` になる。ローダは Npgsql で `UTF8` の文字列を書く。

データベース `keiba` は、この PC の競馬データ倉庫である。提供元ごとにスキーマを分ける。いま作るスキーマ `jv` は、JRA-VAN Data Lab. の JV-Data である。中身は、収集アプリが残す蓄積と速報をローダが解釈した行で、ここでは空のまま作る。テーブルは [schema.md](schema.md) が決める。別の競馬データを足すときは、同じデータベースにスキーマを追加する。スキーマ名は、その提供元が読める名前を、足すときに決める。`jv` のテーブルへは入れない。

接続のホスト、ポート、データベース名、認証はローダの設定に置く。収集アプリの `config.json` には置かない。確認のための接続ユーザはスーパーユーザ `postgres` である。ローダが使うログインロールは `keiba_loader` とする。権限は [design.md](design.md)。

## 2. 入っているもの

| 項目 | 値 |
| --- | --- |
| バージョン | PostgreSQL 16.4 |
| インストール先 | `C:\Program Files\PostgreSQL\16` |
| データディレクトリ | `C:\Program Files\PostgreSQL\16\data` |
| ポート | `5432` |
| サービス | `postgresql-x64-16`。スタートアップは自動 |
| Windows のサービスアカウント | `NT AUTHORITY\NetworkService` |
| スーパーユーザ | `postgres` |
| クラスタのロケール | `Japanese_Japan.932` |
| タイムゾーン | `Asia/Tokyo` |
| `keiba` のエンコーディング | `UTF8` |
| `keiba` のロケール | `C` |

同じインストールに pgAdmin 4 が入っている。定義と確認は `psql` とローダで行う。コマンドは `C:\Program Files\PostgreSQL\16\bin` のフルパスで呼ぶ。システムの `Path` は変えない。

パスワード、データディレクトリの中身、接続文字列はリポジトリに置かない。画面とログにも出さない。

このデータディレクトリには、倉庫以外のデータベースが既にある。`keiba` を作る作業は、データディレクトリを消さず、サービスも入れ替えない。ファイアウォールの `5432` 受信は許可しない。接続元はこの PC のローダだけである。Docker、別 PC、クラウドの PostgreSQL は使わない。

## 3. 待ち受け

確認は次である。`postgres` のパスワードは `psql` が聞く。

```powershell
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -h localhost -p 5432 -d postgres -c "SHOW listen_addresses;"
Get-Service postgresql-x64-16
```

表示は `*`、サービスの状態は `Running` である。`pg_hba.conf` には `127.0.0.1/32` と `::1/128` 以外の `host` 行を足さない。待ち受けを変えるための再起動はしない。

## 4. データベース

```powershell
$psql = "C:\Program Files\PostgreSQL\16\bin\psql.exe"
& $psql -U postgres -h localhost -p 5432 -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE keiba WITH TEMPLATE = template0 ENCODING = 'UTF8' LOCALE = 'C';"
& $psql -U postgres -h localhost -p 5432 -d postgres -v ON_ERROR_STOP=1 -c "COMMENT ON DATABASE keiba IS 'この PC の競馬データ倉庫。提供元ごとにスキーマを分ける。';"
& $psql -U postgres -h localhost -p 5432 -d keiba -v ON_ERROR_STOP=1 -c "CREATE SCHEMA jv;"
& $psql -U postgres -h localhost -p 5432 -d keiba -v ON_ERROR_STOP=1 -c "COMMENT ON SCHEMA jv IS 'JRA-VAN Data Lab. の JV-Data。収集アプリが残す蓄積と速報を、ローダが解釈した行。';"
& $psql -U postgres -h localhost -p 5432 -d keiba -v ON_ERROR_STOP=1 -c "SELECT current_setting('server_version') AS version, current_database() AS database, pg_encoding_to_char(encoding) AS encoding FROM pg_database WHERE datname = current_database();"
```

| 名前 | 種類 | 中身 |
| --- | --- | --- |
| `keiba` | データベース | この PC の競馬データ倉庫。提供元が増えてもデータベースは増やさず、スキーマを足す |
| `jv` | スキーマ | JRA-VAN Data Lab. の JV-Data。今のローダが書く範囲 |

確認の結果は、バージョンが 16.4 で始まり、データベースが `keiba`、エンコーディングが `UTF8` である。スキーマ `jv` とコメントがあることは次で見る。

```powershell
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -h localhost -p 5432 -d keiba -c "\dn+ jv"
```

ローダが接続するときに使う値は次である。パスワードはローダの設定に置き、この表には書かない。

| 項目 | 値 |
| --- | --- |
| ホスト | `localhost` |
| ポート | `5432` |
| データベース | `keiba` |
| スキーマ | `jv` |
| ログインロール | `keiba_loader` |

## 5. 削除

アプリの一覧から PostgreSQL 16 を削除すると、プログラムとサービスは消える。データディレクトリ `C:\Program Files\PostgreSQL\16\data` は残る。このディレクトリには `keiba` 以外のデータベースも入っている。データベースも消すときは、サービスを止めたあと、このディレクトリを別に削除する。

収集アプリの `%AppData%`、`DataRoot` の raw と台帳は、PostgreSQL の削除では触らない。

## 6. 今回決めないこと

- バックアップとリストア
- テーブル、主キー、ビュー。正本は [schema.md](schema.md)
