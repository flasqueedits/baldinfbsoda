# Infinite BSODA - derleme + kurulum scripti
# Kullanim:  .\build.ps1  [-GameDir "D:\Games\BaldiBasics"]  [-Configuration Release]

param(
    [string]$GameDir = "C:\Users\Windows\Downloads\tamam_ulan_asker_collection_14e4c_-VyvA5S (1)\Tamam Ulan Asker Collection\TAMAM ULAN ASKER",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "InfiniteBsoda\InfiniteBsoda.csproj"

# --- Oyun klasorunu coz ---
# 1) Parametre verilmisse onu kullan (mutlak yol veya goreli).
# 2) Verilmemisse / klasor yoksa: indirilen oyun kopyalarini tara ve *_Data icereni bul.
#    Boylece eski bir zip'ten gelen yanlis varsayilan yol derlemeyi bozmaz.
function Resolve-GameDir([string]$dir) {
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        $full = [System.IO.Path]::GetFullPath($dir)
        if (Test-Path $full) { return $full }
        Write-Warning "GameDir bulunamadi: $full"
    }

    $roots = @(
        "C:\Users\Windows\Downloads",
        "C:\Users\Windows\Desktop",
        (Join-Path $env:USERPROFILE "Downloads"),
        (Join-Path $env:USERPROFILE "Desktop")
) | Select-Object -Unique

    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        # 3 seviyeye kadar tara: *_Data klasoru olan ve BepInEx/core iceren oyun kokleri
        $candidates = Get-ChildItem $root -Directory -Recurse -Depth 3 -ErrorAction SilentlyContinue |
            Where-Object {
                (Get-ChildItem $_.FullName -Directory -Filter "*_Data" -ErrorAction SilentlyContinue) -and
                (Test-Path (Join-Path $_.FullName "BepInEx\core"))
            } | Select-Object -First 5

        foreach ($c in $candidates) {
            Write-Host "Oyun klasoru otomatik bulundu: $($c.FullName)" -ForegroundColor Cyan
            return $c.FullName
        }
    }
    return $null
}

$resolved = Resolve-GameDir $GameDir
if (-not $resolved) {
    Write-Error "Oyun klasoru bulunamadi. Lutfen -GameDir ile dogru yolu verin."
    exit 1
}
$GameDir = $resolved

Write-Host "Derleniyor (BaldiDir=$GameDir)" -ForegroundColor Cyan
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
