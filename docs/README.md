# 文書

競馬データ倉庫の要求と構造。実装の入口は [../README.md](../README.md)。

レコードのバイト定義は公式仕様書を正とし、この文書群では再定義しない。状態はいずれもドラフトである。

## 読む順

1. [requirements.md](requirements.md) で、一式として何をするかを見る。
2. [architecture.md](architecture.md) で、収集アプリ、DWH ローダ、予想の実行の境界を見る。
3. 収集アプリを動かす手順は [jv-data-collector/manual.md](jv-data-collector/manual.md)。要求と構造は `jv-data-collector/`、ローダと倉庫は `dwh-loader/` を見る。

## 一覧

| 文書 | 決めること |
| --- | --- |
| [requirements.md](requirements.md) | 一式として何をするか。予想を始める契機と、入力にするフルのデータ |
| [architecture.md](architecture.md) | プロセスの境界とランディング契約 |
| [jv-data-collector/manual.md](jv-data-collector/manual.md) | 収集アプリを入れて、動かし、止める手順 |
| [jv-data-collector/requirements.md](jv-data-collector/requirements.md) | 収集アプリの利用者要求と画面 |
| [jv-data-collector/architecture.md](jv-data-collector/architecture.md) | 収集アプリのプロセス、スレッド、モジュール |
| [jv-data-collector/design.md](jv-data-collector/design.md) | 取得手順、対象データ、保存形式、受入条件 |
| [jv-data-collector/deployment.md](jv-data-collector/deployment.md) | 収集アプリのインストール先、公開、更新、削除 |
| [dwh-loader/requirements.md](dwh-loader/requirements.md) | ローダが満たすこと |
| [dwh-loader/architecture.md](dwh-loader/architecture.md) | ローダの入出力と解釈の責務 |
| [dwh-loader/design.md](dwh-loader/design.md) | 追随の手順、完了ファイルの読み方、展開、受入条件 |
| [dwh-loader/postgresql.md](dwh-loader/postgresql.md) | PostgreSQL 18 を同じ PC に入れる手順 |
| [dwh-loader/schema.md](dwh-loader/schema.md) | データベース定義。テーブル、主キー、有効な版のビュー |
| [reference/](reference/) | 公式 SDK の置き場。このリポジトリには含めない。バイト定義の正本は仕様書 |

予想モデルの式、特徴量、学習手順、結果の保存先は、モデルの式が決まるまで子文書を置かない。データベース定義は [dwh-loader/schema.md](dwh-loader/schema.md)。予想を起動するプロセスの置き方は [architecture.md](architecture.md) の未決に残す。
