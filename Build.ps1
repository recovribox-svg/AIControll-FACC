<#
.SYNOPSIS
    Builds CompanionAI_Pathfinder and produces an installable Unity Mod Manager ZIP.

.DESCRIPTION
    1. Locates the game's managed assemblies (Wrath_Data\Managed) - from -ManagedPath, -GamePath, the WRATH_PATH
       environment variable, Steam auto-detection, or a previously copied References\Managed folder.
    2. Copies the reference assemblies the project needs into References\Managed (git-ignored), so later builds work
       without the game path.
    3. Runs `dotnet build -c Release` (requires the .NET SDK 6 or newer; no Visual Studio needed).
    4. Packages dist\CompanionAI_Pathfinder_v<version>.zip with the same layout as the original release
       (CompanionAI_Pathfinder.dll + Info.json at the archive root) and verifies its contents.
    5. With -Deploy, copies the mod into <GamePath>\Mods\CompanionAI_Pathfinder.

.EXAMPLE
    .\Build.ps1
.EXAMPLE
    .\Build.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure" -Deploy
#>
[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$ManagedPath,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Deploy,
    [switch]$NoCopyReferences
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Project = Join-Path $Root 'CompanionAI_Pathfinder.csproj'
$ModId = 'CompanionAI_Pathfinder'
$GameFolderName = 'Pathfinder Second Adventure'
$RefDir = Join-Path $Root 'References\Managed'

# Assemblies referenced by CompanionAI_Pathfinder.csproj (relative to Wrath_Data\Managed).
$ReferenceFiles = @(
    'Assembly-CSharp.dll', 'Owlcat.Runtime.Core.dll', 'Owlcat.Runtime.UI.dll', 'Owlcat.Runtime.Visual.dll',
    'Owlcat.Runtime.Validation.dll', 'Owlcat.SharedTypes.dll', 'AstarPathfindingProject.dll', 'UniRx.dll',
    'Newtonsoft.Json.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll',
    'UnityEngine.TextRenderingModule.dll', 'UnityEngine.AIModule.dll', 'UnityEngine.PhysicsModule.dll',
    'UnityEngine.AnimationModule.dll', 'UnityModManager\UnityModManager.dll'
)

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Test-ManagedDir([string]$Dir) {
    return $Dir -and (Test-Path (Join-Path $Dir 'Assembly-CSharp.dll'))
}

function Find-SteamGamePath {
    $steamRoots = @()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            $item = Get-ItemProperty -Path $key -ErrorAction Stop
            if ($item.SteamPath) { $steamRoots += $item.SteamPath }
            if ($item.InstallPath) { $steamRoots += $item.InstallPath }
        } catch { }
    }
    $steamRoots += "${env:ProgramFiles(x86)}\Steam"
    $libraries = @()
    foreach ($steam in ($steamRoots | Where-Object { $_ } | Select-Object -Unique)) {
        $steam = $steam -replace '/', '\'
        $libraries += $steam
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($line in Get-Content $vdf) {
                if ($line -match '"path"\s+"([^"]+)"') { $libraries += ($Matches[1] -replace '\\\\', '\') }
            }
        }
    }
    foreach ($lib in ($libraries | Select-Object -Unique)) {
        $candidate = Join-Path $lib "steamapps\common\$GameFolderName"
        if (Test-ManagedDir (Join-Path $candidate 'Wrath_Data\Managed')) { return $candidate }
    }
    return $null
}

# ---------------------------------------------------------------- 1. locate reference assemblies
Write-Step 'Locating game assemblies'
if (-not $GamePath -and $env:WRATH_PATH) { $GamePath = $env:WRATH_PATH }
if (-not $ManagedPath -and $GamePath) { $ManagedPath = Join-Path $GamePath 'Wrath_Data\Managed' }
if (-not $ManagedPath -and (Test-ManagedDir $RefDir)) { $ManagedPath = $RefDir }
if (-not $ManagedPath -and $IsWindows -ne $false) {
    $GamePath = Find-SteamGamePath
    if ($GamePath) { $ManagedPath = Join-Path $GamePath 'Wrath_Data\Managed' }
}
if (-not (Test-ManagedDir $ManagedPath)) {
    throw "Could not find Assembly-CSharp.dll. Pass -GamePath '<...\$GameFolderName>' (or -ManagedPath '<...\Wrath_Data\Managed>'), or set WRATH_PATH."
}
$ManagedPath = (Resolve-Path $ManagedPath).Path
$UmmDll = Join-Path $ManagedPath 'UnityModManager\UnityModManager.dll'
if (-not (Test-Path $UmmDll)) {
    throw "UnityModManager.dll not found at '$UmmDll'. Install Unity Mod Manager into the game first."
}
Write-Host "    Managed: $ManagedPath"

# ---------------------------------------------------------------- 2. copy references (for offline rebuilds)
if (-not $NoCopyReferences -and ($ManagedPath -ne (Resolve-Path $RefDir -ErrorAction SilentlyContinue).Path)) {
    Write-Step "Copying reference assemblies to References\Managed"
    $missing = @()
    foreach ($file in $ReferenceFiles) {
        $src = Join-Path $ManagedPath $file
        if (Test-Path $src) {
            $dst = Join-Path $RefDir $file
            New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
            Copy-Item $src $dst -Force
        } else {
            $missing += $file
        }
    }
    if ($missing.Count -gt 0) { Write-Warning ("Not found in game folder (build may fail): " + ($missing -join ', ')) }
}

# ---------------------------------------------------------------- 3. build
Write-Step "Building ($Configuration)"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "The .NET SDK was not found. Install it (e.g. 'winget install Microsoft.DotNet.SDK.8' or https://dotnet.microsoft.com/download) and re-run."
}
$sdkVersion = (& dotnet --version) 2>$null
if (-not $sdkVersion -or [int]($sdkVersion.Split('.')[0]) -lt 6) {
    throw "The .NET SDK 6 or newer is required (found '$sdkVersion')."
}
& dotnet build $Project -c $Configuration -nologo "-p:ManagedPath=$ManagedPath" "-p:UmmPath=$(Split-Path $UmmDll)"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit code $LASTEXITCODE)." }

$outDir = Join-Path $Root "bin\$Configuration"
$dll = Join-Path $outDir "$ModId.dll"
$infoSrc = Join-Path $Root 'Info.json'
if (-not (Test-Path $dll)) { throw "Build reported success but $dll is missing." }

$info = Get-Content $infoSrc -Raw | ConvertFrom-Json
if ($info.Id -ne $ModId -or $info.AssemblyName -ne "$ModId.dll") { throw "Info.json Id/AssemblyName do not match '$ModId'." }
$version = $info.Version

# ---------------------------------------------------------------- 4. package
Write-Step "Packaging v$version"
$dist = Join-Path $Root 'dist'
$stage = Join-Path $dist $ModId
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $dll $stage
Copy-Item $infoSrc $stage

$zipPath = Join-Path $dist "${ModId}_v$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Entries are written explicitly with flat names so no absolute/backslash paths can end up in the archive.
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @("$ModId.dll", 'Info.json')) {
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $stage $name), $name, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally {
    $zip.Dispose()
}

# verify the archive
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    $expected = @("$ModId.dll", 'Info.json') | Sort-Object
    if (($entries -join '|') -ne ($expected -join '|')) { throw "Unexpected ZIP contents: $($entries -join ', ')" }
    foreach ($e in $zip.Entries) { if ($e.Length -le 0) { throw "ZIP entry $($e.FullName) is empty." } }
} finally {
    $zip.Dispose()
}
Write-Host "    $zipPath"
Write-Host "    contents: $($entries -join ', ')"

# ---------------------------------------------------------------- 5. deploy
if ($Deploy) {
    if (-not $GamePath) {
        # Only derive the game folder from a real <game>\Wrath_Data\Managed path, never from References\Managed.
        $parent = Split-Path $ManagedPath
        if ((Split-Path $ManagedPath -Leaf) -eq 'Managed' -and (Split-Path $parent -Leaf) -eq 'Wrath_Data') {
            $GamePath = Split-Path $parent
        } else {
            throw "-Deploy needs -GamePath (cannot infer the game folder from '$ManagedPath')."
        }
    }
    $modDir = Join-Path $GamePath "Mods\$ModId"
    Write-Step "Deploying to $modDir"
    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    Copy-Item (Join-Path $stage '*') $modDir -Force
}

Write-Step 'Done'
