<#
.SYNOPSIS
  Сборка настольного CashFlow для Windows: self-contained публикация + установщик Inno Setup.

.EXAMPLE
  pwsh tools/publish-desktop.ps1                 # publish в installer/windows/publish и CashFlow-Setup-<версия>.exe в installer/windows/output
  pwsh tools/publish-desktop.ps1 -Version 0.2.0 -SkipInstaller

Требования: .NET 9 SDK с воркодом maui-windows; для установщика — Inno Setup 6.1+ (winget install JRSoftware.InnoSetup).
#>
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\CashFlow.Maui\CashFlow.Maui.csproj"
$out = Join-Path $root "installer\windows\publish"

Write-Host "== Публикация $proj → $out"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish $proj -c $Configuration -f net9.0-windows10.0.19041.0 -r win-x64 --self-contained true `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false `
    -p:ApplicationDisplayVersion=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }

if ($SkipInstaller) { Write-Host "Установщик пропущен (-SkipInstaller)"; return }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup не найден: winget install JRSoftware.InnoSetup" }

$iss = Join-Path $root "installer\windows\CashFlow.iss"
Write-Host "== Установщик: $iscc"
& $iscc "/DAppVersion=$Version" "/DPublishDir=$out" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с кодом $LASTEXITCODE" }
Write-Host "Готово: $(Join-Path $root "installer\windows\output")"
