#Requires -Version 5.1
<#
.SYNOPSIS
    収集アプリを Release で公開し、ローカルのインストール先へミラーする。

.DESCRIPTION
    win-x64 の自己完結フォルダを %TEMP%\KeibaDwh.Collector.publish へ公開し、
    %LocalAppData%\Programs\KeibaDwh\Collector だけを置き換える。
    実行中は通知領域の「終了」を待つ。プロセスは止めない。
    %AppData% と DataRoot は触らない。配置のあと exe は起動しない。

.NOTES
    手順の正本は docs/jv-data-collector/deployment.md。
    リポジトリのルートで次を実行する。
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-collector.ps1
#>
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Fail {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message,
        [int] $Code = 1
    )
    [Console]::Error.WriteLine($Message)
    exit $Code
}

if (-not $env:LOCALAPPDATA) {
    Fail "LOCALAPPDATA がありません。"
}
if (-not $env:TEMP) {
    Fail "TEMP がありません。"
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\JvDataCollector\JvDataCollector.Host\JvDataCollector.Host.csproj"
$stage = Join-Path $env:TEMP "KeibaDwh.Collector.publish"
$dest = Join-Path $env:LOCALAPPDATA "Programs\KeibaDwh\Collector"
$expected = [System.IO.Path]::Combine($env:LOCALAPPDATA, "Programs", "KeibaDwh", "Collector")
if (-not (Test-Path -LiteralPath $project)) {
    Fail "プロジェクトが見つかりません: $project"
}
if (-not [string]::Equals($dest, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail "インストール先が想定と違います: $dest"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "dotnet が PATH にありません。"
}

$running = @(Get-Process -Name "JvDataCollector" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "JvDataCollector が実行中です。通知領域の「終了」で止めてください。終了を待ちます。"
    foreach ($process in $running) {
        Wait-Process -Id $process.Id -ErrorAction SilentlyContinue
    }
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

& dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $stage
if ($LASTEXITCODE -ne 0) {
    Fail "dotnet publish が失敗しました。終了コード $LASTEXITCODE" $LASTEXITCODE
}

$publishedExe = Join-Path $stage "JvDataCollector.exe"
$publishedSqlite = Join-Path $stage "e_sqlite3.dll"
if (-not (Test-Path -LiteralPath $publishedExe) -or -not (Test-Path -LiteralPath $publishedSqlite)) {
    Fail "公開結果に JvDataCollector.exe または e_sqlite3.dll がありません。"
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
& robocopy $stage $dest /MIR /R:2 /W:1 /NP
$copyCode = $LASTEXITCODE
if ($copyCode -ge 8) {
    Fail "robocopy が失敗しました。終了コード $copyCode" $copyCode
}

Write-Host "配置しました: $dest"
exit 0
