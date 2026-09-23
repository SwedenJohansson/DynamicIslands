# Packs the DynamicIslands\ folder into DynamicIslands.rmod (an .rmod is a plain zip; RML compiles the .cs files in game).
# Replaces build.bat, which only works when the repo folder is literally named "DynamicIslands".
# Usage: powershell -ExecutionPolicy Bypass -File pack.ps1 [-Install] [-RaftDir <path>]
param(
    [switch]$Install,
    [string]$RaftDir = "D:\Games\SteamLibrary\steamapps\common\Raft"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$src = Join-Path $PSScriptRoot "DynamicIslands"
$out = Join-Path $PSScriptRoot "DynamicIslands.rmod"

$excludeDirs  = @("bin", "obj", ".vs", "Properties")
$excludeFiles = @("*.csproj", "*.csproj.user", "*.rmod", "*.meta", "*.old", "*.veryold", "*,veryold", "*.pdb")

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
} finally { $zip.Dispose() }

$version = (Get-Content (Join-Path $src "modinfo.json") -Raw | ConvertFrom-Json).version
Write-Host ("Packed {0} files into {1} ({2:N0} KB), version {3}" -f $files.Count, $out, ((Get-Item $out).Length / 1KB), $version) -ForegroundColor Green

if ($Install) {
    $mods = Join-Path $RaftDir "mods"
    if (-not (Test-Path $mods)) { throw "Raft mods folder not found: $mods (install RML and start Raft once)" }
    Copy-Item $out $mods -Force
    Write-Host "Installed to $mods\DynamicIslands.rmod" -ForegroundColor Green
}
