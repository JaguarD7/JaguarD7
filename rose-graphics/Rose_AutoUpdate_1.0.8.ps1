$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$RoseRoot = Join-Path $env:LOCALAPPDATA 'RoseGraphicsDirect'
$Cache = Join-Path $RoseRoot 'cache'
$RuntimeDir = Join-Path $RoseRoot 'dotnet'
$EngineDir = Join-Path $RoseRoot 'engine'
$BundleUrl = 'https://raw.githubusercontent.com/JaguarD7/JaguarD7/37423fe270924fff094e8c38deb63473aee2074c/releases/Rose_DirectRPF_Portable_win-x64.zip'
$BundleSha256 = '1504274B61EB5A05C69D3A2E15A9EAFF0812309B7A313FD0799FA67665D9DA6E'

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

    $stage = Join-Path $RoseRoot 'portable-stage-108'
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
    Copy-Item -Path (Join-Path $srcRuntime '*') -Destination $RuntimeDir -Recurse -Force
    Copy-Item -Path (Join-Path $srcEngine '*') -Destination $EngineDir -Recurse -Force
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

try {
    [System.Windows.Forms.MessageBox]::Show('Rose v1.0.8 is updating automatically. A portable runtime bundle will be downloaded once; no system .NET install is required.','Rose Auto Update','OK','Information') | Out-Null
    Ensure-RosePortableRuntime
    $original = Find-RoseOriginalScript
    $text = Get-Content -LiteralPath $original -Raw -Encoding UTF8
$text = $text.Replace("`$RoseVersion = '1.0.6'","`$RoseVersion = '1.0.8'")
    $text = $text.Replace("`$RoseVersion = '1.0.7'","`$RoseVersion = '1.0.8'")
$text = $text.Replace("`$RoseScriptVersion = '1.0.6'","`$RoseScriptVersion = '1.0.8'")
    $text = $text.Replace("`$RoseScriptVersion = '1.0.7'","`$RoseScriptVersion = '1.0.8'")
$text = $text.Replace('GTA V Legacy - Direct update.rpf / x64b.rpf installer','GTA V Legacy - Direct RPF installer + Auto Update')
$text = $text.Replace('Client-side visual/map files only. No anti-cheat bypass.','Client-side visual/map files only. Portable runtime + automatic Rose updates. No anti-cheat bypass.')
    $text = $text.Replace('جاهز - v1.0.4 Direct RPF','جاهز - v1.0.8 Direct RPF + Auto Update')
    $text = $text.Replace('جاهز - v1.0.7 Direct RPF + Auto Update','جاهز - v1.0.8 Direct RPF + Auto Update')

    $oldInvoke = '$psi.Arguments = ''"'' + $EngineDll + ''" --install "'' + $Oiv + ''" --game "'' + $Gta + ''" --force'''
    $newInvoke = '$psi.Arguments = ''"'' + $EngineDll + ''" --install "'' + $Oiv + ''" --force''' + [Environment]::NewLine + '    $psi.EnvironmentVariables[''ROSE_GTA_PATH''] = $Gta'
    $text = $text.Replace($oldInvoke,$newInvoke)

    $oldErr = 'if ($p.ExitCode -ne 0) { throw "محرك RPF فشل. ExitCode=$($p.ExitCode). تم حفظ التفاصيل في rose-direct.log" }'
    $newErr = 'if ($p.ExitCode -ne 0) { $detail = (($stdout + "`r`n" + $stderr).Trim()); throw ("محرك RPF فشل. ExitCode=" + $p.ExitCode + "`r`n`r`n" + $detail + "`r`n`r`nLog: " + $LogPath) }'
    $text = $text.Replace($oldErr,$newErr)
$marker = '# ROSE_AUTOUPDATE_PORTABLE_108'
if ($text -notmatch [regex]::Escape($marker)) {
    $prefix = @"
$marker
# Portable .NET 8 + RPF engine are provisioned by Rose Auto Update 1.0.8.
# No system-wide .NET installation is required.
"@
    $text = $prefix + "`r`n" + $text
}
    Set-Content -LiteralPath $original -Value $text -Encoding UTF8
    Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $original + '"') -WorkingDirectory (Split-Path $original -Parent) | Out-Null
    exit
} catch {
    [System.Windows.Forms.MessageBox]::Show(('Rose v1.0.8 automatic update failed:' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message),'Rose Auto Update','OK','Error') | Out-Null
    exit 1
}
