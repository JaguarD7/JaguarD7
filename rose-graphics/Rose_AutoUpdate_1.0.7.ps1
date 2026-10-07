$ErrorActionPreference = 'Stop'

$RoseRoot = Join-Path $env:LOCALAPPDATA 'RoseGraphicsDirect'
$Cache = Join-Path $RoseRoot 'cache'
$RuntimeDir = Join-Path $RoseRoot 'dotnet'
$EngineDir = Join-Path $RoseRoot 'engine'
$BundleUrl = 'https://raw.githubusercontent.com/JaguarD7/JaguarD7/1240631299fe33c7d8f3befa5658078f8151fa79/releases/Rose_DirectRPF_Portable_win-x64.zip'
$BundleSha256 = '7506A6BB7BD8E4077DDAD8FC3F759F6344B5BECDBAB5CC147567503C36B36A07'

function Get-RoseSha([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Ensure-RosePortableRuntime {
    $dotnet = Join-Path $RuntimeDir 'dotnet.exe'
    $engine = Join-Path $EngineDir 'CodeWalker.OIVInstaller.dll'
    if ((Test-Path -LiteralPath $dotnet) -and (Test-Path -LiteralPath $engine)) {
        try {
            $r = (& $dotnet --list-runtimes 2>$null | Out-String)
            if ($r -match 'Microsoft\.NETCore\.App 8\.' -and $r -match 'Microsoft\.WindowsDesktop\.App 8\.') { return }
        } catch {}
    }

    New-Item -ItemType Directory -Force -Path $RoseRoot,$Cache | Out-Null
    $zip = Join-Path $Cache 'Rose_DirectRPF_Portable_win-x64.zip'
    if ((Get-RoseSha $zip) -ne $BundleSha256) {
        Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
        $part = $zip + '.part'
        Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
        Invoke-WebRequest -UseBasicParsing -Uri $BundleUrl -OutFile $part -TimeoutSec 300
        $actual = Get-RoseSha $part
        if ($actual -ne $BundleSha256) {
            Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
            throw "Rose portable bundle SHA-256 mismatch. Expected=$BundleSha256 Actual=$actual"
        }
        Move-Item -LiteralPath $part -Destination $zip -Force
    }

    $stage = Join-Path $RoseRoot 'portable-stage-107'
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force

    $srcRuntime = Join-Path $stage 'runtime'
    $srcEngine = Join-Path $stage 'engine'
    if (-not (Test-Path -LiteralPath (Join-Path $srcRuntime 'dotnet.exe'))) { throw 'Portable .NET host missing from Rose bundle.' }
    if (-not (Test-Path -LiteralPath (Join-Path $srcEngine 'CodeWalker.OIVInstaller.dll'))) { throw 'Rose RPF engine missing from bundle.' }

    Remove-Item -LiteralPath $RuntimeDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $EngineDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $RuntimeDir,$EngineDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $srcRuntime '*') -Destination $RuntimeDir -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $srcEngine '*') -Destination $EngineDir -Recurse -Force
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue

    $r = (& (Join-Path $RuntimeDir 'dotnet.exe') --list-runtimes 2>$null | Out-String)
    if ($r -notmatch 'Microsoft\.NETCore\.App 8\.' -or $r -notmatch 'Microsoft\.WindowsDesktop\.App 8\.') { throw 'Rose portable .NET runtime verification failed.' }
}

function Find-RoseOriginalScript {
    $here = (Get-Location).Path
    $p = Join-Path $here 'Rose_Setup.ps1'
    if (Test-Path -LiteralPath $p) { return $p }
    foreach ($root in @((Join-Path $env:USERPROFILE 'Downloads'),(Join-Path $env:USERPROFILE 'Desktop'))) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $hit = Get-ChildItem -LiteralPath $root -Filter 'Rose_Setup.ps1' -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw 'Rose_Setup.ps1 was not found. Keep the EXE and PS1 together in the extracted Rose folder.'
}

Ensure-RosePortableRuntime
$original = Find-RoseOriginalScript
$text = Get-Content -LiteralPath $original -Raw -Encoding UTF8
$text = $text.Replace("`$RoseVersion = '1.0.6'","`$RoseVersion = '1.0.7'")
$text = $text.Replace("`$RoseScriptVersion = '1.0.6'","`$RoseScriptVersion = '1.0.7'")
$text = $text.Replace('GTA V Legacy - Direct update.rpf / x64b.rpf installer','GTA V Legacy - Direct RPF installer + Auto Update')
$text = $text.Replace('Client-side visual/map files only. No anti-cheat bypass.','Client-side visual/map files only. Portable runtime + automatic Rose updates. No anti-cheat bypass.')

$marker = '# ROSE_AUTOUPDATE_PORTABLE_107'
if ($text -notmatch [regex]::Escape($marker)) {
    $prefix = @"
$marker
# Portable .NET 8 + RPF engine are provisioned by Rose Auto Update 1.0.7.
# No system-wide .NET installation is required.
"@
    $text = $prefix + "`r`n" + $text
}
Set-Content -LiteralPath $original -Value $text -Encoding UTF8

Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $original + '"') -WorkingDirectory (Split-Path $original -Parent) | Out-Null
exit
