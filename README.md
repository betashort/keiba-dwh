# 競馬データ倉庫

契約者の PC に JRA-VAN Data Lab. の JV-Data を残し、書き込みが完了した生データを PostgreSQL へ自動で載せ、開催日の速報が揃ったときに馬券予想モデルをフルデータで自動実行する一式。

文書の入口は [docs/README.md](docs/README.md)。一式として何をするかは [docs/requirements.md](docs/requirements.md)、プロセスの境界は [docs/architecture.md](docs/architecture.md)。

## 仕事の分担

| 役割 | 状態 | すること |
| --- | --- | --- |
| JV-Data 収集アプリ | `src/JvDataCollector` | ログオン中に常駐し、JV-Link から届くバイト列と台帳をローカルに残す。PostgreSQL には接続しない |
| DWH ローダ | `src/DwhLoader` | 完了した生データだけを読み、解釈した行を同一 PC の PostgreSQL のスキーマ `jv` へ書く。設計は [docs/dwh-loader/design.md](docs/dwh-loader/design.md) |
| 予想の実行 | 未実装 | 開始契機の速報が載り終え、対象レースのフルのデータが揃ったとき、馬券予想モデルを起動する。プロセスの置き方は未決 |

投票、購入、収支の計算はしない。JV-Link 本体、利用キー、JV-Data は同梱しない。レコードのバイト定義は公式仕様書を正とする。

## リポジトリ

```
docs/                  要求、構造、設計
src/JvDataCollector/   収集アプリ（C# / Windows Forms / x86 / net10.0-windows）
  JvDataCollector.Host          トレイ、状態、初回設定
  JvDataCollector.Application   スケジュール、セッション、水位
  JvDataCollector.JvLink        JV-Link の COM
  JvDataCollector.Storage       設定、raw、SQLite
  JvDataCollector.Tests
src/DwhLoader/         DWH ローダ（C# / Windows Forms / x64 / net10.0-windows）
  DwhLoader.Host                状態、設定、周回
  DwhLoader.Application         ファイルの選択、展開、写しの中身
  DwhLoader.Storage             台帳の読み取り、PostgreSQL への書き込み
  DwhLoader.Structs             SDK の C# 構造体。JVData_Struct.cs を同梱する
  DwhLoader.Tests
  sql/                          スーパーユーザが適用する jv とロール。ローダは実行しない
```

公式 SDK の zip、仕様書、JV-Link は `docs/reference/` に置き、このリポジトリには含めない。収集アプリには同梱しない。ローダが展開に使う C# 構造体定義だけは `DwhLoader.Structs` にコピーしてある。

## ビルド

収集アプリは日本語版 Windows 11、.NET 10、公式の JV-Link 5.0.0（32bit）を前提にする。JV-Link は利用者が別に導入する。使用する PostgreSQL 16 は [docs/dwh-loader/postgresql.md](docs/dwh-loader/postgresql.md)。

```powershell
dotnet build src/JvDataCollector/JvDataCollector.slnx
dotnet test src/JvDataCollector/JvDataCollector.slnx
dotnet build src/DwhLoader/DwhLoader.slnx
dotnet test src/DwhLoader/DwhLoader.slnx
```

インストール先へ出すときは、Release の自己完結フォルダを公開してからミラーする。`dotnet build` の出力はコピーしない。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-collector.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-loader.ps1
```

収集アプリの操作は [docs/jv-data-collector/manual.md](docs/jv-data-collector/manual.md)。配置と更新は [docs/jv-data-collector/deployment.md](docs/jv-data-collector/deployment.md)。ローダの公開と差し替えは [docs/dwh-loader/design.md](docs/dwh-loader/design.md)。

## ライセンス

[MIT License](LICENSE)。JRA-VAN Data Lab. の SDK、JV-Link、JV-Data の権利はそれぞれの権利者に属する。
