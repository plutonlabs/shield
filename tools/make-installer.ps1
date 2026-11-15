<#
.SYNOPSIS
  Builds the Windows installer (PlutonShield-v<version>-win-<arch>-Setup.exe) from dist\<arch>\ with Inno Setup.
  Run tools\build.ps1 first. Inno Setup 6 is installed with Chocolatey if it is missing (as on GitHub's Windows runners).
#>
param(
    [ValidateSet('x64', 'arm64')] [string] $Arch = 'x64',
    # e.g. 0.4.0 or 0.4.0-rc.1 (the number after the dash is not part of the Windows file version)
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')] [string] $Version,
    [string] $OutDir = 'release',
    # Quicker, larger installer: for test builds that nobody downloads as a release.
    [switch] $Fast
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$source = Join-Path $root "dist/$Arch"
if (-not (Test-Path (Join-Path $source 'PlutonShield.exe'))) {
    throw "dist/$Arch/PlutonShield.exe was not found. Run tools/build.ps1 -Arch $Arch first."
}

function Find-Iscc {
    $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    $folders = @(
        [Environment]::GetFolderPath('ProgramFilesX86'),
        [Environment]::GetFolderPath('ProgramFiles'),
        (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs')
    )
    foreach ($folder in $folders) {
        $candidate = Join-Path $folder 'Inno Setup 6/ISCC.exe'
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
        throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again.'
    }
    Write-Host 'Installing Inno Setup with Chocolatey...'
    choco install innosetup --yes --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Installing Inno Setup failed.' }
    $iscc = Find-Iscc
    if (-not $iscc) { throw 'Inno Setup was installed but ISCC.exe was not found.' }
}

$out = Join-Path $root $OutDir
New-Item -ItemType Directory -Force $out | Out-Null
$numeric = ($Version -replace '-.*$', '') + '.0'

# Typed array, then splatted: each element stays one argument.
[string[]] $isccArgs = @("/DAppVersion=$Version", "/DNumericVersion=$numeric", "/DArch=$Arch", "/DSourceDir=$source", "/DOutputDir=$out")
if ($Fast) { $isccArgs += '/DFast=1' }
$isccArgs += (Join-Path $root 'installer/PlutonShield.iss')
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }

$setup = Join-Path $out "PlutonShield-v$Version-win-$Arch-Setup.exe"
if (-not (Test-Path $setup)) { throw "The installer was not created: $setup" }
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path -Leaf $setup)" | Set-Content "$setup.sha256" -Encoding ascii
Write-Host "Built: $setup"
