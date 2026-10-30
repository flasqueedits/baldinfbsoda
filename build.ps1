# Infinite BSODA - derleme + kurulum scripti
# Kullanim:  .\build.ps1  [-GameDir "D:\Games\BaldiBasics"]  [-Configuration Release]

param(
    [string]$GameDir = "C:\Users\Windows\Downloads\tamam_ulan_asker_collection_14e4c_-VyvA5S (1)\Tamam Ulan Asker Collection\TAMAM ULAN ASKER",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "InfiniteBsoda\InfiniteBsoda.csproj"

dotnet build $project -c $Configuration "-p:BaldiDir=$GameDir"
if ($LASTEXITCODE -ne 0) { Write-Error "Derleme basarisiz oldu."; exit 1 }

# *_Data klasorunu bul, plugins hedefini hazirla
$dataDir = Get-ChildItem $GameDir -Directory -Filter "*_Data" | Select-Object -First 1
if (-not $dataDir) { Write-Error "Oyun klasorunde *_Data bulunamadi: $GameDir"; exit 1 }

$pluginsDir = Join-Path $GameDir "BepInEx\plugins"
if (-not (Test-Path (Join-Path $GameDir "BepInEx\core"))) {
    Write-Warning "BepInEx kurulu gorunmuyor! once BepInEx 5 (win x64) kurun: https://github.com/BepInEx/BepInEx/releases"
}
New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null

$dll = Join-Path $PSScriptRoot "InfiniteBsoda\bin\$Configuration\net472\InfiniteBsoda.dll"
Copy-Item $dll $pluginsDir -Force

Write-Host "Kuruldu: $pluginsDir\InfiniteBsoda.dll" -ForegroundColor Green
