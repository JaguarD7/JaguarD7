$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$RoseRoot = Join-Path $env:LOCALAPPDATA 'RoseGraphicsDirect'
$StatePath = Join-Path $RoseRoot 'state.json'
$NewVersion = '1.0.9'
$EngineUrl = 'https://raw.githubusercontent.com/JaguarD7/JaguarD7/28715e45bb6899a4ba85a07a41eab445ed08119f/releases/Rose_ModsRPF_Portable_v109_win-x64.zip'
$EngineSha256 = '0CE5D10929453386C8D2A7786A7B37BD8C014354464956B0044F3B15699D19D2'

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
    throw 'Rose_Setup.ps1 was not found. Keep Rose_Setup.exe and Rose_Setup.ps1 together in the extracted folder.'
}
function Get-Sha([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}
function Restore-DirectRoseOriginals {
    if (-not (Test-Path -LiteralPath $StatePath)) { return $false }
    try { $st = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $false }
    if (-not $st.gtaPath -or -not $st.backupDir) { return $false }
    if (-not $st.installedUpdateHash -and -not $st.installedX64bHash) { return $false }
    $gta = [string]$st.gtaPath
    $backup = [string]$st.backupDir
    $gameUpdate = Join-Path $gta 'update\update.rpf'
    $gameX64b = Join-Path $gta 'x64b.rpf'
    $bu = Join-Path $backup 'update.rpf'
    $bx = Join-Path $backup 'x64b.rpf'
    if (-not (Test-Path -LiteralPath $bu)) { return $false }

    $roseDirect = $false
    if ($st.installedUpdateHash -and (Get-Sha $gameUpdate) -eq ([string]$st.installedUpdateHash).ToUpperInvariant()) { $roseDirect = $true }
    if ($st.installedX64bHash -and (Get-Sha $gameX64b) -eq ([string]$st.installedX64bHash).ToUpperInvariant()) { $roseDirect = $true }
    if (-not $roseDirect) { return $false }

    Copy-Item -LiteralPath $bu -Destination $gameUpdate -Force
    if (Test-Path -LiteralPath $bx) { Copy-Item -LiteralPath $bx -Destination $gameX64b -Force }
    if ((Get-Sha $gameUpdate) -ne (Get-Sha $bu)) { throw 'Automatic restore of original update.rpf failed.' }
    if ((Test-Path -LiteralPath $bx) -and (Get-Sha $gameX64b) -ne (Get-Sha $bx)) { throw 'Automatic restore of original x64b.rpf failed.' }
    return $true
}

$modsHelpers = @'
function Get-ModArchiveInfo([string]$Gta) {
    $modsRoot = Join-Path $Gta 'mods'
    return [pscustomobject]@{
        Root = $modsRoot
        Update = Join-Path $modsRoot 'update\update.rpf'
        X64B = Join-Path $modsRoot 'x64b.rpf'
    }
}
function Backup-ModsBeforeRose([string]$Gta,[string]$BackupDir) {
    $mods = Get-ModArchiveInfo $Gta
    $dir = Join-Path $BackupDir 'mods_before_rose'
    $metaPath = Join-Path $dir 'meta.json'
    if (Test-Path -LiteralPath $metaPath) {
        try { return (Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json) } catch {}
    }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $hadUpdate = Test-Path -LiteralPath $mods.Update
    $hadX64b = Test-Path -LiteralPath $mods.X64B
    if ($hadUpdate) {
        Set-Status 'Backup: mods\update\update.rpf الحالي...'
        Copy-Item -LiteralPath $mods.Update -Destination (Join-Path $dir 'update.rpf') -Force
    }
    if ($hadX64b) {
        Set-Status 'Backup: mods\x64b.rpf الحالي...'
        Copy-Item -LiteralPath $mods.X64B -Destination (Join-Path $dir 'x64b.rpf') -Force
    }
    $meta = [pscustomobject]@{ hadUpdate=[bool]$hadUpdate; hadX64b=[bool]$hadX64b; createdAt=(Get-Date).ToString('o') }
    $meta | ConvertTo-Json | Set-Content -LiteralPath $metaPath -Encoding UTF8
    return $meta
}
function Restore-ModsBeforeRose([string]$Gta,[string]$BackupDir) {
    $mods = Get-ModArchiveInfo $Gta
    $dir = Join-Path $BackupDir 'mods_before_rose'
    $metaPath = Join-Path $dir 'meta.json'
    if (-not (Test-Path -LiteralPath $metaPath)) { return }
    $meta = Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([bool]$meta.hadUpdate) {
        $src = Join-Path $dir 'update.rpf'
        if (-not (Test-Path -LiteralPath $src)) { throw 'نسخة mods\update\update.rpf الاحتياطية مفقودة.' }
        New-Item -ItemType Directory -Force -Path (Split-Path $mods.Update -Parent) | Out-Null
        Copy-Item -LiteralPath $src -Destination $mods.Update -Force
    } else { Remove-Item -LiteralPath $mods.Update -Force -ErrorAction SilentlyContinue }
    if ([bool]$meta.hadX64b) {
        $src = Join-Path $dir 'x64b.rpf'
        if (-not (Test-Path -LiteralPath $src)) { throw 'نسخة mods\x64b.rpf الاحتياطية مفقودة.' }
        New-Item -ItemType Directory -Force -Path (Split-Path $mods.X64B -Parent) | Out-Null
        Copy-Item -LiteralPath $src -Destination $mods.X64B -Force
    } else { Remove-Item -LiteralPath $mods.X64B -Force -ErrorAction SilentlyContinue }
}
function Ensure-VanillaOriginals([string]$Gta,[string]$BackupDir) {
    $a = Get-ArchiveInfo $Gta
    $bu = Join-Path $BackupDir 'update.rpf'
    if (-not (Test-Path -LiteralPath $bu)) { throw 'Backup update.rpf الأصلي غير موجود.' }
    $expectedU = Get-Sha256 $bu
    $currentU = Get-Sha256 $a.Update
    $bx = Join-Path $BackupDir 'x64b.rpf'
    $expectedX = if (Test-Path -LiteralPath $bx) { Get-Sha256 $bx } else { $null }
    $currentX = if ($a.HasX64B) { Get-Sha256 $a.X64B } else { $null }
    if ($currentU -ne $expectedU -or ($expectedX -and $currentX -ne $expectedX)) {
        $state = Get-State
        $safeRoseMatch = $false
        if ($state) {
            if ($state.installedUpdateHash -and $currentU -eq ([string]$state.installedUpdateHash).ToUpperInvariant()) { $safeRoseMatch = $true }
            if ($state.installedX64bHash -and $currentX -eq ([string]$state.installedX64bHash).ToUpperInvariant()) { $safeRoseMatch = $true }
        }
        if (-not $safeRoseMatch) { throw 'ملفات GTA الأصلية ما زالت معدلة بتعديل غير معروف. استخدم Verify/Repair للعبة قبل التثبيت.' }
        Set-Status 'FiveM رفض الملفات الأصلية المعدلة؛ أرجع النسخة الأصلية الآن...'
        Restore-FromBackupInternal $Gta $BackupDir
    }
    if ((Get-Sha256 $a.Update) -ne $expectedU) { throw 'update.rpf الأصلية لم ترجع كما كانت.' }
    if ($expectedX -and (Get-Sha256 $a.X64B) -ne $expectedX) { throw 'x64b.rpf الأصلية لم ترجع كما كانت.' }
}
'@

$portableBlock = @'
function Ensure-PortableEngine {
    $portableRoot = Join-Path $Root 'portable-v109'
    $dotnet = Join-Path $portableRoot 'runtime\dotnet.exe'
    $dll = Join-Path $portableRoot 'engine\CodeWalker.OIVInstaller.dll'
    if ((Test-Path -LiteralPath $dotnet) -and (Test-Path -LiteralPath $dll)) {
        try {
            $r = (& $dotnet --list-runtimes 2>$null | Out-String)
            if ($r -match 'Microsoft\.NETCore\.App 8\.' -and $r -match 'Microsoft\.WindowsDesktop\.App 8\.') { return [pscustomobject]@{ DotNet=$dotnet; EngineDll=$dll } }
        } catch {}
    }
    Set-Status 'تنزيل Rose RPF Engine + .NET Portable (مرة واحدة فقط، حوالي 71 MB)...'
    $zip = Join-Path $Cache 'Rose_ModsRPF_Portable_v109_win-x64.zip'
    Download-Checked $EngineUrl $zip $EngineSha256
    Remove-Item -LiteralPath $portableRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $portableRoot | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $portableRoot -Force
    if (-not (Test-Path -LiteralPath $dotnet)) { throw 'dotnet.exe المحمول غير موجود بعد فك Rose Engine.' }
    if (-not (Test-Path -LiteralPath $dll)) { throw 'CodeWalker.OIVInstaller.dll غير موجود بعد فك Rose Engine.' }
    $r = (& $dotnet --list-runtimes 2>$null | Out-String)
    if ($r -notmatch 'Microsoft\.NETCore\.App 8\.' -or $r -notmatch 'Microsoft\.WindowsDesktop\.App 8\.') { throw 'فشل التحقق من .NET Portable داخل Rose.' }
    return [pscustomobject]@{ DotNet=$dotnet; EngineDll=$dll }
}
'@

$installBlock = @'
function Invoke-RpfInstall([string]$DotNet,[string]$EngineDll,[string]$Oiv,[string]$Gta) {
    $outLog = Join-Path $Root 'engine-output.log'
    $errLog = Join-Path $Root 'engine-error.log'
    Remove-Item $outLog,$errLog -Force -ErrorAction SilentlyContinue
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $DotNet
    $psi.Arguments = '"' + $EngineDll + '" --install "' + $Oiv + '" --force'
    $psi.EnvironmentVariables['ROSE_GTA_PATH'] = $Gta
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = New-Object Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    Set-Content -LiteralPath $outLog -Value $stdout -Encoding UTF8
    Set-Content -LiteralPath $errLog -Value $stderr -Encoding UTF8
    Write-Log "Engine exit code: $($p.ExitCode)"
    Write-Log $stdout
    if ($stderr) { Write-Log $stderr }
    if ($p.ExitCode -ne 0) {
        $nl = [Environment]::NewLine
        $detail = (($stdout + $nl + $stderr).Trim())
        throw ("محرك RPF فشل. ExitCode=" + $p.ExitCode + $nl + $nl + $detail + $nl + $nl + "Log: " + $LogPath)
    }
}
function Install-RoseDirect {
    Ensure-NotRunning
    $gta = Resolve-GtaPath
    $a = Get-ArchiveInfo $gta
    Set-Status "GTA V Legacy: $gta"
    $backupDir = Ensure-OriginalBackup $gta
    $stateBefore = Get-State
    $originalUpdateHash = if ($stateBefore -and $stateBefore.originalUpdateHash) { [string]$stateBefore.originalUpdateHash } else { Get-Sha256 (Join-Path $backupDir 'update.rpf') }
    $originalX64bHash = $null
    if (Test-Path -LiteralPath (Join-Path $backupDir 'x64b.rpf')) {
        $originalX64bHash = if ($stateBefore -and $stateBefore.originalX64bHash) { [string]$stateBefore.originalX64bHash } else { Get-Sha256 (Join-Path $backupDir 'x64b.rpf') }
    }
    try {
        Ensure-VanillaOriginals $gta $backupDir
        [void](Backup-ModsBeforeRose $gta $backupDir)
        $mods = Get-ModArchiveInfo $gta
        $beforeModsUpdate = Get-Sha256 $mods.Update
        $beforeModsX64b = Get-Sha256 $mods.X64B
        $portable = Ensure-PortableEngine
        Set-Status 'إنشاء Rose Cinematic Ultra للحفظ داخل GTA\mods...'
        $oiv = Build-OivPackage
        Set-Status 'تركيب Rose داخل mods\update\update.rpf و mods\x64b.rpf...'
        Invoke-RpfInstall $portable.DotNet $portable.EngineDll $oiv $gta
        $afterModsUpdate = Get-Sha256 $mods.Update
        $afterModsX64b = Get-Sha256 $mods.X64B
        if (-not $afterModsUpdate) { throw 'لم يتم إنشاء mods\update\update.rpf.' }
        if ($afterModsUpdate -eq $beforeModsUpdate -and $afterModsX64b -eq $beforeModsX64b) { throw 'ملفات mods لم تتغير؛ لن أعتبر التثبيت ناجحاً.' }
        if ((Get-Sha256 $a.Update) -ne $originalUpdateHash.ToUpperInvariant()) { throw 'update.rpf الأصلية تغيرت أثناء التثبيت.' }
        if ($originalX64bHash -and (Get-Sha256 $a.X64B) -ne $originalX64bHash.ToUpperInvariant()) { throw 'x64b.rpf الأصلية تغيرت أثناء التثبيت.' }
        $state = [pscustomobject]@{
            version=$RoseVersion; mode='mods'; gtaPath=$gta; backupDir=$backupDir; installed=$true; installedAt=(Get-Date).ToString('o');
            originalUpdateHash=$originalUpdateHash; originalX64bHash=$originalX64bHash; modsRoot=$mods.Root; modsUpdatePath=$mods.Update;
            modsX64bPath=$mods.X64B; installedModsUpdateHash=$afterModsUpdate; installedModsX64bHash=$afterModsX64b; updatePath=$a.Update; x64bPath=$a.X64B
        }
        Save-State $state
        Set-Status 'Rose مثبت داخل GTA\mods والـRPF الأصلية Vanilla.'
        $nl = [Environment]::NewLine
        Show-Info ('تم الإصلاح والتثبيت.' + $nl + $nl + 'Original update.rpf: Vanilla + verified' + $nl + 'Original x64b.rpf: Vanilla + verified' + $nl + 'mods\update\update.rpf: Rose ACTIVE' + $nl + $nl + 'الآن شغل FiveM من جديد.')
    } catch {
        Write-Log "INSTALL FAILED: $($_.Exception.Message)"
        try { Restore-FromBackupInternal $gta $backupDir; Restore-ModsBeforeRose $gta $backupDir; Write-Log 'Automatic rollback completed.' } catch { Write-Log "ROLLBACK FAILED: $($_.Exception.Message)" }
        throw
    }
}
function Restore-Rose {
    Ensure-NotRunning
    $state = Get-State
    if (-not $state -or -not $state.gtaPath -or -not $state.backupDir) { throw 'لا توجد نسخة احتياطية مسجلة من Rose.' }
    $gta = [string]$state.gtaPath
    if (-not (Test-LegacyGtaPath $gta)) { throw 'مسار GTA V المحفوظ غير صالح.' }
    Restore-FromBackupInternal $gta ([string]$state.backupDir)
    Restore-ModsBeforeRose $gta ([string]$state.backupDir)
    $a = Get-ArchiveInfo $gta
    if ($state.originalUpdateHash -and (Get-Sha256 $a.Update) -ne ([string]$state.originalUpdateHash).ToUpperInvariant()) { throw 'فشل التحقق من update.rpf الأصلية.' }
    if ($state.originalX64bHash -and (Get-Sha256 $a.X64B) -ne ([string]$state.originalX64bHash).ToUpperInvariant()) { throw 'فشل التحقق من x64b.rpf الأصلية.' }
    $state.installed = $false
    $state.restoredAt = (Get-Date).ToString('o')
    Save-State $state
    Set-Status 'تم استرجاع GTA الأصلية و mods السابقة.'
    Show-Info 'تم استرجاع ملفات GTA الأصلية وإرجاع مجلد mods إلى حالته السابقة.'
}
function Diagnose-Rose {
    try {
        $gta = Resolve-GtaPath
        $a = Get-ArchiveInfo $gta
        $mods = Get-ModArchiveInfo $gta
        $state = Get-State
        $uHash = Get-Sha256 $a.Update
        $xHash = if ($a.HasX64B) { Get-Sha256 $a.X64B } else { 'N/A' }
        $muHash = Get-Sha256 $mods.Update
        $originalOk = $false
        if ($state -and $state.originalUpdateHash -and $uHash -eq ([string]$state.originalUpdateHash).ToUpperInvariant()) {
            $originalOk = $true
            if ($state.originalX64bHash -and $a.HasX64B -and $xHash -ne ([string]$state.originalX64bHash).ToUpperInvariant()) { $originalOk = $false }
        }
        $modsActive = $false
        if ($state -and $state.installed -and $state.installedModsUpdateHash -and $muHash -eq ([string]$state.installedModsUpdateHash).ToUpperInvariant()) { $modsActive = $true }
        $backupOk = $false
        if ($state -and $state.backupDir) { $backupOk = Test-Path -LiteralPath (Join-Path ([string]$state.backupDir) 'update.rpf') }
        $status = if ($originalOk -and $modsActive) { 'READY FOR FIVEM - ROSE MODS ACTIVE' } elseif (-not $originalOk) { 'WARNING: ORIGINAL GTA RPF MODIFIED' } else { 'ORIGINAL OK / ROSE MODS NOT ACTIVE' }
        $nl = [Environment]::NewLine
        Show-Info ('Status: ' + $status + $nl + $nl + 'Original RPF vanilla: ' + $originalOk + $nl + 'Rose mods active: ' + $modsActive + $nl + 'Backup valid: ' + $backupOk + $nl + 'Version: ' + $RoseVersion)
    } catch { Show-Error $_.Exception.Message }
}
'@

try {
    [System.Windows.Forms.MessageBox]::Show('Rose v1.0.9: FiveM رفض تعديل x64b.rpf الأصلي. سيتم إرجاع ملفات GTA الأصلية ثم تحويل Rose تلقائياً إلى GTA\mods.','Rose Auto Update','OK','Information') | Out-Null
    $original = Find-RoseOriginalScript
    $text = Get-Content -LiteralPath $original -Raw -Encoding UTF8

    $text = [regex]::Replace($text, "\$RoseVersion\s*=\s*'[^']+'", "`$RoseVersion = '1.0.9'", 1)
    $text = [regex]::Replace($text, "\$RoseScriptVersion\s*=\s*'[^']+'", "`$RoseScriptVersion = '1.0.9'", 1)
    $text = [regex]::Replace($text, "\$EngineUrl\s*=\s*'[^']+'", "`$EngineUrl = '$EngineUrl'", 1)
    $text = [regex]::Replace($text, "\$EngineSha256\s*=\s*'[^']+'", "`$EngineSha256 = '$EngineSha256'", 1)

    if ($text -notmatch 'function Get-ModArchiveInfo') {
        $text = $text.Replace('function Get-RawUrl([string]$Repo,[string]$Ref,[string]$Path) {', $modsHelpers + [Environment]::NewLine + 'function Get-RawUrl([string]$Repo,[string]$Ref,[string]$Path) {')
    }
    $text = [regex]::Replace($text, '(?s)function Ensure-Engine \{.*?(?=function Get-MapFiles \{)', [Text.RegularExpressions.MatchEvaluator]{ param($m) $portableBlock + [Environment]::NewLine })
    $text = [regex]::Replace($text, '(?s)function Ensure-PortableEngine \{.*?(?=function Get-MapFiles \{)', [Text.RegularExpressions.MatchEvaluator]{ param($m) $portableBlock + [Environment]::NewLine })
    $text = [regex]::Replace($text, '(?s)function Invoke-RpfInstall\(.*?(?=function Check-Updates \{)', [Text.RegularExpressions.MatchEvaluator]{ param($m) $installBlock + [Environment]::NewLine })

    $text = $text.Replace('Rose Cinematic Ultra - GTA V Legacy Direct','Rose Cinematic Ultra - GTA V Legacy / FiveM Mods')
    $text = $text.Replace('GTA V Legacy - Direct update.rpf / x64b.rpf installer','GTA V Legacy - FiveM-safe mods folder installer')
    $text = $text.Replace('GTA V Legacy - Direct RPF installer + Auto Update','GTA V Legacy - FiveM-safe mods folder installer + Auto Update')
    $text = [regex]::Replace($text, "\$warning\.Text\s*=\s*'[^']*'", "`$warning.Text = 'FiveM-safe mode: ملفات GTA الأصلية تبقى Vanilla، وRose يثبت داخل GTA\\mods مع Backup كامل.'", 1)
    $text = $text.Replace("New-Button 'تثبيت / إصلاح Rose داخل ملفات GTA'", "New-Button 'تثبيت / إصلاح Rose داخل GTA\\mods'")
    $text = [regex]::Replace($text, "\$script:StatusLabel\.Text\s*=\s*'[^']*'", "`$script:StatusLabel.Text = 'جاهز - v1.0.9 FiveM-safe mods folder'", 1)
    $text = [regex]::Replace($text, "\$foot\.Text\s*=\s*'[^']*'", "`$foot.Text = 'Original game RPFs stay vanilla. Rose uses GTA\\mods. No anti-cheat bypass.'", 1)
    $text = $text.Replace('Rose Cinematic Ultra Direct','Rose Cinematic Ultra Mods')
    $text = $text.Replace('Legacy Direct','Legacy Mods')

    $marker = '# ROSE_FIVEM_MODS_MIGRATION_109'
    if ($text -notmatch [regex]::Escape($marker)) { $text = $marker + [Environment]::NewLine + $text }
    Set-Content -LiteralPath $original -Value $text -Encoding UTF8

    $restored = Restore-DirectRoseOriginals
    if ($restored) {
        [System.Windows.Forms.MessageBox]::Show('تم إرجاع update.rpf و x64b.rpf الأصلية. خطأ FiveM Detected modified game files تم تنظيف سببه. الآن اضغط تثبيت Rose ليتم تركيبه داخل GTA\mods.','Rose v1.0.9','OK','Information') | Out-Null
    }
    Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $original + '"') -WorkingDirectory (Split-Path $original -Parent) | Out-Null
    exit 0
} catch {
    [System.Windows.Forms.MessageBox]::Show(('Rose v1.0.9 update failed:' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message),'Rose Auto Update','OK','Error') | Out-Null
    exit 1
}
