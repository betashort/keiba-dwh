# PostgreSQL の導入

- 状態: ドラフト
- 日付: 2026-11-07
- 要求: [requirements.md](requirements.md)
- 構造: [architecture.md](architecture.md)
- データベース定義書: [schema.md](schema.md)
- 位置づけ: 収集アプリと同じ PC に PostgreSQL 18 を入れる手順。テーブル、主キー、ビューは [schema.md](schema.md)。ローダ用ロールの権限とバックアップは未決。レコードのバイト定義は公式仕様書を正とし、ここでは再定義しない。

## 1. 結論

倉庫は、契約者本人の日本語版 Windows 11 で動く PostgreSQL 18 である。入手元は [PostgreSQL の Windows インストーラ](https://www.postgresql.org/download/windows/) が案内する EDB のインストーラとし、そのページに出る 18 系の現行マイナーを入れる。18 のコミュニティサポートは 2030-11-14 までである。

サーバは Windows サービス `postgresql-x64-18` として常駐し、待ち受けは `localhost` の `5432` だけにする。データベース名は `keiba`、エンコーディングは `UTF8`、ロケールは `C` とする。スキーマ `jv` はこの導入で空のまま作る。テーブルは [schema.md](schema.md) が決める。

接続のホスト、ポート、データベース名、認証はローダの設定に置く。収集アプリの `config.json` には置かない。確認のための接続ユーザはスーパーユーザ `postgres` である。ローダが使うログインロールは、権限が決まるまで定めない。

## 2. 入れるもの

管理者としてインストーラを実行する。収集アプリの配置は管理者権限を要らない。PostgreSQL のサービスと `C:\Program Files` へのインストールは要る。

| 項目 | 値 |
| --- | --- |
| インストーラ | EDB の Windows x86-64。32bit は配布されない |
| コンポーネント | PostgreSQL Server、Command Line Tools |
| インストール先 | `C:\Program Files\PostgreSQL\18` |
| データディレクトリ | `C:\Program Files\PostgreSQL\18\data` |
| ポート | `5432` |
| ロケール | `C`。OS の既定にしない |
| スーパーユーザ | `postgres`。パスワードはインストーラで決める |
| サービス | `postgresql-x64-18`。スタートアップは自動 |
| Windows のサービスアカウント | ローカルユーザ `postgres`。パスワードはデータベースのスーパーユーザと同じ |

ロケールを `C` にするのは、日本語版 Windows の既定 `Japanese_Japan.932` がデータベースのエンコーディングを `WIN932` に固定するためである。ローダは Npgsql で `UTF8` の文字列を書く。

パスワード、データディレクトリの中身、接続文字列はリポジトリに置かない。画面とログにも出さない。

入れないもの:

| 対象 | 理由 |
| --- | --- |
| pgAdmin 4 | 定義と確認は `psql` とローダで行う |
| Stack Builder | サーバ側の追加ドライバは要らない。接続はローダの Npgsql |
| ファイアウォールの 5432 受信許可 | 接続元はこの PC のローダだけである |
| Docker、別 PC、クラウドの PostgreSQL | 倉庫は収集アプリと同じ PC に置く |

インストーラはシステムの `Path` を変えない。コマンドは `C:\Program Files\PostgreSQL\18\bin` のフルパスで呼ぶ。

## 3. インストール

1. [Windows installers](https://www.postgresql.org/download/windows/) から、PostgreSQL 18 の Windows x86-64 インストーラを取る。
2. 管理者として実行し、Next で進める。
3. インストール先は `C:\Program Files\PostgreSQL\18` のままにする。
4. コンポーネントは PostgreSQL Server と Command Line Tools だけを残す。
5. データディレクトリは `C:\Program Files\PostgreSQL\18\data` のままにする。
6. スーパーユーザ `postgres` のパスワードを決める。既存の Windows ユーザ `postgres` があるときは、そのパスワードと揃える。
7. ポートは `5432` のままにする。
8. Locale は `C` を選ぶ。一覧の先頭にある OS の既定ロケールは選ばない。
9. 要約を確認してインストールする。終了時の Stack Builder は外す。
10. インストーラが Windows ファイアウォールの受信を許可するか聞いたときは、許可しない。

## 4. 待ち受け

EDB のインストーラは `listen_addresses` を `*` に書き換える。導入のあと、ループバックだけに戻す。

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h localhost -p 5432 -d postgres -c "SHOW listen_addresses;"
```

表示が `localhost` なら、この節の編集は要らない。`*` のときは、管理者で `C:\Program Files\PostgreSQL\18\data\postgresql.conf` を開き、次の行にする。

```
listen_addresses = 'localhost'
```

`pg_hba.conf` には `127.0.0.1/32` と `::1/128` 以外の `host` 行を足さない。保存したあと、サービスを再起動する。

```powershell
Restart-Service postgresql-x64-18
```

再起動後に同じ `SHOW listen_addresses;` が `localhost` であることと、サービスの状態が `Running` であることを確認する。

```powershell
Get-Service postgresql-x64-18
```

## 5. データベース

`postgres` のパスワードは `psql` が聞く。コマンドラインとスクリプトには書かない。

```powershell
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
& $psql -U postgres -h localhost -p 5432 -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE keiba WITH TEMPLATE = template0 ENCODING = 'UTF8' LOCALE = 'C';"
& $psql -U postgres -h localhost -p 5432 -d keiba -v ON_ERROR_STOP=1 -c "CREATE SCHEMA jv;"
& $psql -U postgres -h localhost -p 5432 -d keiba -v ON_ERROR_STOP=1 -c "SELECT current_setting('server_version') AS version, current_database() AS database, pg_encoding_to_char(encoding) AS encoding FROM pg_database WHERE datname = current_database();"
```

確認の結果は、バージョンが 18 で始まり、データベースが `keiba`、エンコーディングが `UTF8` である。スキーマ `jv` があることは次で見る。

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h localhost -p 5432 -d keiba -c "\dn jv"
```

ローダが接続するときに使う値は次である。ユーザとパスワードは、ロールが決まるまで空欄のままにする。

| 項目 | 値 |
| --- | --- |
| ホスト | `localhost` |
| ポート | `5432` |
| データベース | `keiba` |
| スキーマ | `jv` |

## 6. 削除

アプリの一覧から PostgreSQL 18 を削除すると、プログラムとサービスは消える。データディレクトリ `C:\Program Files\PostgreSQL\18\data` は残る。データベースも消すときは、サービスを止めたあと、このディレクトリを別に削除する。

収集アプリの `%AppData%`、`DataRoot` の raw と台帳は、PostgreSQL の削除では触らない。

## 7. 今回決めないこと

- ローダ用ログインロールの名前と権限
- バックアップとリストア
- テーブル、主キー、ビュー。正本は [schema.md](schema.md)
- ローダのインストール先と、完了した生データへ自動で追随する実装
