#Requires -Version 5.1
<#
.SYNOPSIS
    DWH ローダを Release で公開し、ローカルのインストール先へミラーする。

.DESCRIPTION
    win-x64 の自己完結フォルダを %TEMP%\KeibaDwh.Loader.publish へ公開し、
    %LocalAppData%\Programs\KeibaDwh\Loader だけを置き換える。
    実行中は状態の窓の「停止」を待つ。プロセスは止めない。
    %AppData%、DataRoot、PostgreSQL は触らない。配置のあと exe は起動しない。

.NOTES
    手順の正本は docs/dwh-loader/design.md。
    リポジトリのルートで次を実行する。
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-loader.ps1
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
$project = Join-Path $root "src\DwhLoader\DwhLoader.Host\DwhLoader.Host.csproj"
$stage = Join-Path $env:TEMP "KeibaDwh.Loader.publish"
$dest = Join-Path $env:LOCALAPPDATA "Programs\KeibaDwh\Loader"
$expected = [System.IO.Path]::Combine($env:LOCALAPPDATA, "Programs", "KeibaDwh", "Loader")
if (-not (Test-Path -LiteralPath $project)) {
    Fail "プロジェクトが見つかりません: $project"
}
if (-not [string]::Equals($dest, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail "インストール先が想定と違います: $dest"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "dotnet が PATH にありません。"
}

$running = @(Get-Process -Name "DwhLoader" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "DwhLoader が実行中です。状態の窓の「停止」で止めてください。終了を待ちます。"
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

$publishedExe = Join-Path $stage "DwhLoader.exe"
$publishedSqlite = Join-Path $stage "e_sqlite3.dll"
if (-not (Test-Path -LiteralPath $publishedExe) -or -not (Test-Path -LiteralPath $publishedSqlite)) {
    Fail "公開結果に DwhLoader.exe または e_sqlite3.dll がありません。"
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
& robocopy $stage $dest /MIR /R:2 /W:1 /NP
$copyCode = $LASTEXITCODE
if ($copyCode -ge 8) {
    Fail "robocopy が失敗しました。終了コード $copyCode" $copyCode
}

Write-Host "配置しました: $dest"
exit 0
