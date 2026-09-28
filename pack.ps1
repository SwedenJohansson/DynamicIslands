# Packs the DynamicIslands\ folder into DynamicIslands.rmod (an .rmod is a plain zip; RML compiles the .cs files in game).
# Replaces build.bat, which only works when the repo folder is literally named "DynamicIslands".
# Usage: powershell -ExecutionPolicy Bypass -File pack.ps1 [-Install] [-Release] [-RaftDir <path>]
#   -Release leaves out DevTests*.cs (the CI* dev/test console commands)
param(
    [switch]$Install,
    [switch]$Release,
    [string]$RaftDir = "D:\Games\SteamLibrary\steamapps\common\Raft"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$src = Join-Path $PSScriptRoot "DynamicIslands"
$out = Join-Path $PSScriptRoot "DynamicIslands.rmod"

$excludeDirs  = @("bin", "obj", ".vs", "Properties")
$excludeFiles = @("*.csproj", "*.csproj.user", "*.rmod", "*.meta", "*.old", "*.veryold", "*,veryold", "*.pdb")
if ($Release) { $excludeFiles += "DevTests*.cs" }

$files = Get-ChildItem $src -Recurse -File | Where-Object {
    $rel = $_.FullName.Substring($src.Length + 1)
    $top = $rel.Split('\')[0]
    if ($excludeDirs -contains $top) { return $false }
    foreach ($pattern in $excludeFiles) { if ($_.Name -like $pattern) { return $false } }
    return $true
}

if (Test-Path $out) { Remove-Item $out }
$zip = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in $files) {
        $entry = $f.FullName.Substring($src.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
    # The guide as a PDF, for the Guide buttons in the game (HelpLinks.OpenGuide writes it out of the .rmod to open it)
    $pdf = Join-Path $PSScriptRoot "docs\Custom-Islands-Guide.pdf"
    if (Test-Path $pdf) { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $pdf, "Custom-Islands-Guide.pdf", [System.IO.Compression.CompressionLevel]::Optimal) }
    else { Write-Warning "docs\Custom-Islands-Guide.pdf is missing: the Guide buttons will open the guide online" }
} finally { $zip.Dispose() }

$version = (Get-Content (Join-Path $src "modinfo.json") -Raw | ConvertFrom-Json).version
Write-Host ("Packed {0} files into {1} ({2:N0} KB), version {3}{4}" -f $files.Count, $out, ((Get-Item $out).Length / 1KB), $version, $(if ($Release) { ", release (no dev commands)" } else { "" })) -ForegroundColor Green

if ($Install) {
    $mods = Join-Path $RaftDir "mods"
    if (-not (Test-Path $mods)) { throw "Raft mods folder not found: $mods (install RML and start Raft once)" }
    Copy-Item $out $mods -Force
    Write-Host "Installed to $mods\DynamicIslands.rmod" -ForegroundColor Green
}
