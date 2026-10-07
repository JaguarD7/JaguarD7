# Rose Cinematic Ultra - FiveM client RPF package installer
# v1.0.11 - installs a real OPEN RPF package into FiveM.app\mods.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.Windows.Forms.Application]::EnableVisualStyles()

$RoseVersion = '1.0.11'
$RoseScriptVersion = '1.0.11'
$ManifestUrl = 'https://raw.githubusercontent.com/JaguarD7/JaguarD7/main/rose-graphics/manifest.json'
$ToolsUrl = 'https://raw.githubusercontent.com/JaguarD7/JaguarD7/e177794cbe20eb853cd561fcb3e24f3f3969417e/releases/Rose_FiveM_PackageTools_v111_win-x64.zip'
$ToolsSha256 = 'C96DDB7634FE73B2518EDA6A0C448ED205AAC50CA16B1AA6723F83BFBA05AC3D'
$GraphicsRepo = 'spencerj14/Fivem-Visual-Pack-v2'
$GraphicsRef = '918ccc880725871a137ed7d9be5ede6b8399111f'
$GraphicsRoot = 'Visual Pack/appdata/citizen/common/data'
$MapRepo = 'jordantoulain/black-and-white-map'
$MapRef = '2069f8f19f8bc011212289e3b666ab7b5eddd406'
$MapRoot = 'San Andreas (without Cayo Bridge)/stream'
$MapZoomUrl = 'https://raw.githubusercontent.com/dlucre/BluebirdRPLauncher/fb3f94d44931a6e05e9c6a0a4229d93c6be96bb7/BlueBirdLauncherUI/assets/FiveM/coloredmapfiles/ui/mapzoomdata.meta'

$Root = Join-Path $env:LOCALAPPDATA 'RoseGraphicsDirect'
$Cache = Join-Path $Root 'cache'
$BuildRoot = Join-Path $Root 'build-v111'
$ToolsRoot = Join-Path $Root 'tools-v111'
$StatePath = Join-Path $Root 'state.json'
$LogPath = Join-Path $Root 'rose-v111.log'
New-Item -ItemType Directory -Force -Path $Root,$Cache | Out-Null

function Write-Log([string]$Text) {
    Add-Content -LiteralPath $LogPath -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $Text) -Encoding UTF8
}
function Show-Info([string]$Text) {
    Write-Log ('INFO: ' + $Text)
    [System.Windows.Forms.MessageBox]::Show($Text,'Rose Cinematic Ultra','OK','Information') | Out-Null
}
function Show-Error([string]$Text) {
    Write-Log ('ERROR: ' + $Text)
    [System.Windows.Forms.MessageBox]::Show($Text,'Rose Cinematic Ultra','OK','Error') | Out-Null
}
function Set-Status([string]$Text) {
    if ($script:StatusLabel) {
        $script:StatusLabel.Text = $Text
        $script:StatusLabel.Refresh()
        [System.Windows.Forms.Application]::DoEvents()
    }
    Write-Log $Text
}
function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}
function Load-State {
    if (-not (Test-Path -LiteralPath $StatePath)) { return $null }
    try { return (Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}
function Save-State($State) {
    $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $StatePath -Encoding UTF8
}
function Test-IsAdmin {
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $p = New-Object Security.Principal.WindowsPrincipal($id)
        return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { return $false }
}
if (-not (Test-IsAdmin)) {
    try {
        Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList ('-NoProfile -STA -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"') | Out-Null
    } catch {
        [System.Windows.Forms.MessageBox]::Show('Rose يحتاج صلاحية Administrator.','Rose Cinematic Ultra','OK','Error') | Out-Null
    }
    exit
}

trap {
    try { Show-Error ('Rose startup/runtime error:' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message + [Environment]::NewLine + [Environment]::NewLine + 'Log: ' + $LogPath) } catch {}
    exit 1
}

function Get-RemoteManifest {
    try {
        $tmp = Join-Path $env:TEMP ('rose_manifest_' + [guid]::NewGuid().ToString('N') + '.json')
        Invoke-WebRequest -UseBasicParsing -Uri $ManifestUrl -OutFile $tmp -TimeoutSec 20
        $m = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 | ConvertFrom-Json
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        return $m
    } catch {
        Write-Log ('Manifest unavailable: ' + $_.Exception.Message)
        return $null
    }
}
function Try-SelfUpdate {
    try {
        $m = Get-RemoteManifest
        if (-not $m) { return }
        if (-not $m.installer_script_version -or -not $m.installer_script_url -or -not $m.installer_script_sha256) { return }
        $remote = [version][string]$m.installer_script_version
        $local = [version]$RoseScriptVersion
        if ($remote -le $local) { return }
        $dir = Join-Path $Root 'app'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $dst = Join-Path $dir ('Rose_Setup_' + [string]$m.installer_script_version + '.ps1')
        Invoke-WebRequest -UseBasicParsing -Uri ([string]$m.installer_script_url) -OutFile $dst -TimeoutSec 60
        if ((Get-Sha256 $dst) -ne ([string]$m.installer_script_sha256).ToUpperInvariant()) {
            Remove-Item -LiteralPath $dst -Force -ErrorAction SilentlyContinue
            throw 'فشل التحقق من ملف تحديث Rose.'
        }
        Start-Process powershell.exe -WindowStyle Hidden -WorkingDirectory $dir -ArgumentList ('-NoProfile -STA -ExecutionPolicy Bypass -File "' + $dst + '"') | Out-Null
        exit
    } catch {
        Write-Log ('Self update skipped: ' + $_.Exception.Message)
    }
}
Try-SelfUpdate

function Test-LegacyGta([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $Path 'GTA5.exe'))) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $Path 'update\update.rpf'))) { return $false }
    if (Test-Path -LiteralPath (Join-Path $Path 'GTA5_Enhanced.exe')) { return $false }
    if (Test-Path -LiteralPath (Join-Path $Path 'eboot.bin')) { return $false }
    return $true
}
function Find-GtaLegacy {
    $s = Load-State
    if ($s -and $s.gtaPath -and (Test-LegacyGta ([string]$s.gtaPath))) { return [string]$s.gtaPath }
    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($k in @(
        'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V',
        'HKLM:\SOFTWARE\Rockstar Games\Grand Theft Auto V',
        'HKCU:\SOFTWARE\Rockstar Games\Grand Theft Auto V'
    )) {
        try {
            $x = Get-ItemProperty $k -ErrorAction Stop
            foreach ($n in @('InstallFolder','InstallFolderSteam','InstallLocation')) {
                if ($x.$n) { $candidates.Add([string]$x.$n) }
            }
        } catch {}
    }
    foreach ($d in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        foreach ($rel in @(
            'Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V',
            'SteamLibrary\steamapps\common\Grand Theft Auto V',
            'Program Files\Rockstar Games\Grand Theft Auto V',
            'Epic Games\GTAV'
        )) { $candidates.Add((Join-Path $d.Root $rel)) }
    }
    foreach ($p in ($candidates | Select-Object -Unique)) {
        if (Test-LegacyGta $p) { return (Resolve-Path $p).Path }
    }
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'اختر مجلد GTA V Legacy الذي يحتوي GTA5.exe'
    if ($dlg.ShowDialog() -eq 'OK' -and (Test-LegacyGta $dlg.SelectedPath)) { return $dlg.SelectedPath }
    throw 'لم أجد GTA V Legacy.'
}
function Find-FiveMApp {
    $preferred = Join-Path $env:LOCALAPPDATA 'FiveM\FiveM.app'
    if (Test-Path -LiteralPath (Join-Path $preferred 'CitizenFX.ini')) { return $preferred }
    if (Test-Path -LiteralPath $preferred) { return $preferred }
    $root = Join-Path $env:LOCALAPPDATA 'FiveM'
    if (Test-Path -LiteralPath $root) {
        $hit = Get-ChildItem -LiteralPath $root -Filter CitizenFX.ini -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) { return $hit.DirectoryName }
    }
    throw 'لم أجد FiveM.app.'
}
function Ensure-Closed {
    $busy = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'FiveM*' -or $_.ProcessName -like 'GTA5*' -or $_.ProcessName -eq 'PlayGTAV' }
    if ($busy) { throw 'اقفل FiveM و GTA V بالكامل ثم حاول مرة ثانية.' }
}
function Raw-Url([string]$Repo,[string]$Ref,[string]$Path) {
    $parts = $Path -split '/'
    $encoded = ($parts | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
    return 'https://raw.githubusercontent.com/' + $Repo + '/' + $Ref + '/' + $encoded
}
function Download-File([string]$Url,[string]$Dest,[string]$Sha) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Dest -Parent) | Out-Null
    if ($Sha -and (Test-Path -LiteralPath $Dest) -and (Get-Sha256 $Dest) -eq $Sha.ToUpperInvariant()) { return }
    $part = $Dest + '.part'
    Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $part -TimeoutSec 300
    if ((Get-Item -LiteralPath $part).Length -lt 16) { throw ('ملف التنزيل ناقص: ' + $Url) }
    if ($Sha) {
        $actual = Get-Sha256 $part
        if ($actual -ne $Sha.ToUpperInvariant()) {
            Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
            throw ('SHA-256 غير مطابق: ' + $Url)
        }
    }
    Move-Item -LiteralPath $part -Destination $Dest -Force
}
function Ensure-Tools {
    $dotnet = Join-Path $ToolsRoot 'runtime\dotnet.exe'
    $packer = Join-Path $ToolsRoot 'packer\RoseRpfPacker.dll'
    if ((Test-Path -LiteralPath $dotnet) -and (Test-Path -LiteralPath $packer)) {
        try {
            $r = (& $dotnet --list-runtimes 2>$null | Out-String)
            if ($r -match 'Microsoft\.NETCore\.App 8\.') { return [pscustomobject]@{ DotNet=$dotnet; Packer=$packer } }
        } catch {}
    }
    Set-Status 'تنزيل أدوات Rose RPF (مرة واحدة فقط، حوالي 74 MB)...'
    $zip = Join-Path $Cache 'Rose_FiveM_PackageTools_v111_win-x64.zip'
    Download-File $ToolsUrl $zip $ToolsSha256
    Remove-Item -LiteralPath $ToolsRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $ToolsRoot | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $ToolsRoot -Force
    if (-not (Test-Path -LiteralPath $dotnet)) { throw 'dotnet.exe غير موجود في أدوات Rose.' }
    if (-not (Test-Path -LiteralPath $packer)) { throw 'RoseRpfPacker.dll غير موجود.' }
    return [pscustomobject]@{ DotNet=$dotnet; Packer=$packer }
}

$GraphicsFiles = @(
    'cloudkeyframes.xml',
    'clouds.xml',
    'cpqsmix_ssaosettings.xml',
    'effects/vfxlightningsettings.xml',
    'hbaosettings.xml',
    'lensflare_f.xml',
    'lensflare_m.xml',
    'lensflare_t.xml',
    'levels/gta5/weather.xml',
    'levels/gta5/weather/blizzard_emitter_mist.xml',
    'levels/gta5/weather/blizzard_render_mist.xml',
    'levels/gta5/weather/desert_emitter_ground_heavy.xml',
    'levels/gta5/weather/desert_render_ground.xml',
    'levels/gta5/weather/desert_render_ground_heavy.xml',
    'levels/gta5/weather/rainstorm_render_ground.xml',
    'levels/gta5/weather/snowheavy_emitter_ground.xml',
    'levels/gta5/weather/snowheavy_render_ground.xml',
    'timecycle/timecycle_mods_1.xml',
    'timecycle/timecycle_mods_2.xml',
    'timecycle/timecycle_mods_3.xml',
    'timecycle/timecycle_mods_4.xml',
    'timecycle/underwater_deep.xml',
    'timecycle/w_blizzard.xml',
    'timecycle/w_clear.xml',
    'timecycle/w_clearing.xml',
    'timecycle/w_clouds.xml',
    'timecycle/w_extrasunny.xml',
    'timecycle/w_foggy.xml',
    'timecycle/w_halloween.xml',
    'timecycle/w_neutral.xml',
    'timecycle/w_overcast.xml',
    'timecycle/w_rain.xml',
    'timecycle/w_smog.xml',
    'timecycle/w_snow.xml',
    'timecycle/w_snowlight.xml',
    'timecycle/w_thunder.xml',
    'timecycle/w_xmas.xml',
    'visualsettings.dat',
    'watertune.xml'
)
$MapFiles = @(
    'minimap_0_0.ytd',
    'minimap_0_1.ytd',
    'minimap_0_2.ydd',
    'minimap_0_3.ydd',
    'minimap_0_4.ydd',
    'minimap_0_5.ydd',
    'minimap_0_6.ydd',
    'minimap_1_0.ytd',
    'minimap_1_1.ydd',
    'minimap_1_1.ytd',
    'minimap_1_2.ydd',
    'minimap_1_3.ydd',
    'minimap_1_4.ydd',
    'minimap_1_5.ydd',
    'minimap_1_6.ydd',
    'minimap_1_7.ydd',
    'minimap_1_8.ydd',
    'minimap_2_0.ydd',
    'minimap_2_0.ytd',
    'minimap_2_1.ydd',
    'minimap_2_1.ytd',
    'minimap_2_2.ydd',
    'minimap_2_3.ydd',
    'minimap_2_4.ydd',
    'minimap_2_5.ydd',
    'minimap_2_6.ydd',
    'minimap_2_7.ydd',
    'minimap_2_8.ydd',
    'minimap_3_0.ydd',
    'minimap_3_1.ydd',
    'minimap_3_2.ydd',
    'minimap_3_3.ydd',
    'minimap_3_4.ydd',
    'minimap_3_5.ydd',
    'minimap_3_6.ydd',
    'minimap_3_7.ydd',
    'minimap_3_8.ydd',
    'minimap_4_0.ydd',
    'minimap_4_1.ydd',
    'minimap_4_2.ydd',
    'minimap_4_3.ydd',
    'minimap_4_4.ydd',
    'minimap_4_5.ydd',
    'minimap_4_6.ydd',
    'minimap_4_7.ydd',
    'minimap_4_8.ydd',
    'minimap_5_0.ydd',
    'minimap_5_1.ydd',
    'minimap_5_2.ydd',
    'minimap_5_3.ydd',
    'minimap_5_4.ydd',
    'minimap_5_5.ydd',
    'minimap_5_6.ydd',
    'minimap_5_7.ydd',
    'minimap_5_8.ydd',
    'minimap_6_0.ydd',
    'minimap_6_1.ydd',
    'minimap_6_2.ydd',
    'minimap_6_3.ydd',
    'minimap_6_4.ydd',
    'minimap_6_5.ydd',
    'minimap_6_6.ydd',
    'minimap_6_7.ydd',
    'minimap_6_8.ydd',
    'minimap_7_0.ydd',
    'minimap_7_1.ydd',
    'minimap_7_2.ydd',
    'minimap_7_3.ydd',
    'minimap_7_4.ydd',
    'minimap_7_5.ydd',
    'minimap_7_6.ydd',
    'minimap_lod_128.ytd',
    'minimap_sea_0_0.ytd',
    'minimap_sea_0_1.ytd',
    'minimap_sea_1_0.ytd',
    'minimap_sea_1_1.ytd',
    'minimap_sea_2_0.ytd',
    'minimap_sea_2_1.ytd'
)

function Test-Rsc7([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    try {
        $b = [IO.File]::ReadAllBytes($Path)
        if ($b.Length -lt 4) { return $false }
        return ([Text.Encoding]::ASCII.GetString($b,0,4) -eq 'RSC7')
    } catch { return $false }
}
function Replace-Line([string]$Text,[string]$Key,[string]$Value) {
    $pattern = '(?m)^\s*' + [regex]::Escape($Key) + '.*$'
    if ([regex]::IsMatch($Text,$pattern)) { return [regex]::Replace($Text,$pattern,($Key + [char]9 + $Value),1) }
    return $Text + [Environment]::NewLine + $Key + [char]9 + $Value + [Environment]::NewLine
}
function Replace-Tag([string]$Text,[string]$Tag,[string]$Value) {
    $pattern = '(?s)(<' + [regex]::Escape($Tag) + '>).*?(</' + [regex]::Escape($Tag) + '>)'
    if (-not [regex]::IsMatch($Text,$pattern)) { return $Text }
    return [regex]::Replace($Text,$pattern,{ param($m) $m.Groups[1].Value + $Value + $m.Groups[2].Value },1)
}
function Tune-Graphics([string]$GraphicsDir) {
    $vs = Join-Path $GraphicsDir 'visualsettings.dat'
    $text = Get-Content -LiteralPath $vs -Raw -Encoding UTF8
    $text = Replace-Line $text 'rain.NumberParticles' '4500'
    $text = Replace-Line $text 'rain.UseLitShader' '1.00'
    $text = Replace-Line $text 'rain.ambient' '0.48'
    $text = Replace-Line $text 'rain.wrapScale' '0.68'
    $text = Replace-Line $text 'rain.wrapBias' '0.32'
    $text = Replace-Line $text 'heightReflect.width' '180.00'
    $text = Replace-Line $text 'heightReflect.height' '260.00'
    $text = Replace-Line $text 'heightReflect.specularoffset' '0.18'
    $text = Replace-Line $text 'car.emissiveMultiplier' '2.25'
    Set-Content -LiteralPath $vs -Value $text -Encoding UTF8

    foreach ($rel in ($GraphicsFiles | Where-Object { $_ -like 'timecycle/w_*.xml' })) {
        $p = Join-Path $GraphicsDir ($rel -replace '/','\')
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $x = Get-Content -LiteralPath $p -Raw -Encoding UTF8
        $wet = ($rel -match 'w_rain|w_thunder|w_clearing')
        $fog = ($rel -match 'w_foggy|w_smog|w_overcast|w_rain|w_thunder|w_clearing')
        $amb = if ($wet) { '3.0000 3.0000 3.0000 3.0000 3.0000 3.0000 3.0000 3.0000 3.0000 3.0000 3.0000 3.0000 3.0000' } else { '2.4000 2.4000 2.4000 2.4000 2.4000 2.4000 2.4000 2.4000 2.4000 2.4000 2.4000 2.4000 2.4000' }
        $em = if ($wet) { '3.3000 3.3000 3.3000 3.3000 3.3000 3.3000 3.3000 3.3000 3.3000 3.3000 3.3000 3.3000 3.3000' } else { '2.7000 2.7000 2.7000 2.7000 2.7000 2.7000 2.7000 2.7000 2.7000 2.7000 2.7000 2.7000 2.7000' }
        $hdr = if ($wet) { '1.9000 1.8500 1.6000 1.4500 1.3000 1.2500 1.2500 1.2500 1.3000 1.4500 1.6500 1.8500 1.9000' } else { '1.6000 1.5000 1.2500 1.1000 1.0000 0.9500 0.9500 0.9500 1.0000 1.1000 1.2500 1.4500 1.6000' }
        $water = if ($wet) { '0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000' } else { '0.3500 0.3500 0.3500 0.3500 0.3500 0.3500 0.3500 0.3500 0.3500 0.3500 0.3500 0.3500 0.3500' }
        $x = Replace-Tag $x 'reflection_tweak_exterior_amb' $amb
        $x = Replace-Tag $x 'reflection_tweak_emissive' $em
        $x = Replace-Tag $x 'reflection_hdr_mult' $hdr
        $x = Replace-Tag $x 'water_reflection' $water
        if ($fog) {
            $x = Replace-Tag $x 'fogray_intensity' '0.1800 0.1800 0.2200 0.2800 0.3200 0.3600 0.3600 0.3600 0.3200 0.2800 0.2400 0.2000 0.1800'
        }
        Set-Content -LiteralPath $p -Value $x -Encoding UTF8
    }
}
function Prepare-PackageSource {
    Remove-Item -LiteralPath $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue
    $src = Join-Path $BuildRoot 'src'
    $content = Join-Path $src 'content'
    $graphics = Join-Path $content 'graphics'
    $maps = Join-Path $content 'maps'
    New-Item -ItemType Directory -Force -Path $graphics,$maps | Out-Null

    $index = 0
    foreach ($rel in $GraphicsFiles) {
        $index++
        Set-Status ('تنزيل ملفات الجرافيكس ' + $index + '/' + $GraphicsFiles.Count + ': ' + $rel)
        $dest = Join-Path $graphics ($rel -replace '/','\')
        $url = Raw-Url $GraphicsRepo $GraphicsRef ($GraphicsRoot + '/' + $rel)
        Download-File $url $dest $null
    }
    Tune-Graphics $graphics

    $index = 0
    foreach ($name in $MapFiles) {
        $index++
        Set-Status ('تنزيل الخريطة والميني ماب ' + $index + '/' + $MapFiles.Count + ': ' + $name)
        $dest = Join-Path $maps $name
        $url = Raw-Url $MapRepo $MapRef ($MapRoot + '/' + $name)
        Download-File $url $dest $null
        if (-not (Test-Rsc7 $dest)) { throw ('ملف خريطة غير صالح: ' + $name) }
    }
    Download-File $MapZoomUrl (Join-Path $content 'mapzoomdata.meta') $null
    Build-Assembly $src
    return $src
}
function Build-Assembly([string]$SourceRoot) {
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
    [void]$sb.AppendLine('<package version="2.2" id="{31C30C8F-01B7-4AE7-A0D5-7A1105D6C111}" target="Five">')
    [void]$sb.AppendLine('  <metadata>')
    [void]$sb.AppendLine('    <name>Rose Cinematic Ultra</name>')
    [void]$sb.AppendLine('    <version><major>1</major><minor>11</minor><tag>FiveM Client</tag></version>')
    [void]$sb.AppendLine('    <author><displayName>Rose</displayName></author>')
    [void]$sb.AppendLine('    <description><![CDATA[Cinematic graphics plus monochrome map/minimap for FiveM Legacy.]]></description>')
    [void]$sb.AppendLine('  </metadata>')
    [void]$sb.AppendLine('  <colors><headerBackground useBlackTextColor="False">$FF191919</headerBackground><iconBackground>$FF555555</iconBackground></colors>')
    [void]$sb.AppendLine('  <content>')
    [void]$sb.AppendLine('    <archive path="update\update.rpf" createIfNotExist="False" type="RPF7">')
    foreach ($rel in $GraphicsFiles) {
        $src = 'graphics\' + ($rel -replace '/','\')
        $dst = 'common\data\' + ($rel -replace '/','\')
        [void]$sb.AppendLine(('      <add source="' + $src + '">' + $dst + '</add>'))
    }
    [void]$sb.AppendLine('      <add source="mapzoomdata.meta">common\data\ui\mapzoomdata.meta</add>')
    [void]$sb.AppendLine('    </archive>')
    [void]$sb.AppendLine('    <archive path="x64b.rpf" createIfNotExist="False" type="RPF7">')
    [void]$sb.AppendLine('      <archive path="data\cdimages\scaleform_generic.rpf" createIfNotExist="False" type="RPF7">')
    foreach ($name in $MapFiles) {
        [void]$sb.AppendLine(('        <add source="maps\' + $name + '">' + $name + '</add>'))
    }
    [void]$sb.AppendLine('      </archive>')
    [void]$sb.AppendLine('    </archive>')
    [void]$sb.AppendLine('  </content>')
    [void]$sb.AppendLine('</package>')
    Set-Content -LiteralPath (Join-Path $SourceRoot 'assembly.xml') -Value $sb.ToString() -Encoding UTF8
}
function Cleanup-Old-Rose([string]$Gta) {
    $s = Load-State
    if (-not $s) { return }
    try {
        if ($s.backupDir) {
            $backup = [string]$s.backupDir
            $bu = Join-Path $backup 'update.rpf'
            $bx = Join-Path $backup 'x64b.rpf'
            $gu = Join-Path $Gta 'update\update.rpf'
            $gx = Join-Path $Gta 'x64b.rpf'
            if ($s.installedUpdateHash -and (Test-Path -LiteralPath $bu) -and (Get-Sha256 $gu) -eq ([string]$s.installedUpdateHash).ToUpperInvariant()) {
                Set-Status 'إرجاع update.rpf الأصلية من نسخة Rose القديمة...'
                Copy-Item -LiteralPath $bu -Destination $gu -Force
            }
            if ($s.installedX64bHash -and (Test-Path -LiteralPath $bx) -and (Get-Sha256 $gx) -eq ([string]$s.installedX64bHash).ToUpperInvariant()) {
                Set-Status 'إرجاع x64b.rpf الأصلية من نسخة Rose القديمة...'
                Copy-Item -LiteralPath $bx -Destination $gx -Force
            }

            $oldMods = Join-Path $backup 'mods_before_rose'
            $metaPath = Join-Path $oldMods 'meta.json'
            if (Test-Path -LiteralPath $metaPath) {
                try {
                    $meta = Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
                    $mu = Join-Path $Gta 'mods\update\update.rpf'
                    $mx = Join-Path $Gta 'mods\x64b.rpf'
                    if ([bool]$meta.hadUpdate -and (Test-Path -LiteralPath (Join-Path $oldMods 'update.rpf'))) {
                        New-Item -ItemType Directory -Force -Path (Split-Path $mu -Parent) | Out-Null
                        Copy-Item -LiteralPath (Join-Path $oldMods 'update.rpf') -Destination $mu -Force
                    } elseif (-not [bool]$meta.hadUpdate) {
                        Remove-Item -LiteralPath $mu -Force -ErrorAction SilentlyContinue
                    }
                    if ([bool]$meta.hadX64b -and (Test-Path -LiteralPath (Join-Path $oldMods 'x64b.rpf'))) {
                        New-Item -ItemType Directory -Force -Path (Split-Path $mx -Parent) | Out-Null
                        Copy-Item -LiteralPath (Join-Path $oldMods 'x64b.rpf') -Destination $mx -Force
                    } elseif (-not [bool]$meta.hadX64b) {
                        Remove-Item -LiteralPath $mx -Force -ErrorAction SilentlyContinue
                    }
                } catch {
                    Write-Log ('Old GTA mods cleanup skipped: ' + $_.Exception.Message)
                }
            }
        }
    } catch {
        Write-Log ('Old Rose cleanup skipped: ' + $_.Exception.Message)
    }
}
function Backup-Existing-Package([string]$Target) {
    $dir = Join-Path $Root 'backup\fivem-mods-v111'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $metaPath = Join-Path $dir 'meta.json'
    if (Test-Path -LiteralPath $metaPath) {
        try { return (Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json) } catch {}
    }
    $had = Test-Path -LiteralPath $Target
    if ($had) { Copy-Item -LiteralPath $Target -Destination (Join-Path $dir 'Rose_Cinematic_Ultra.rpf') -Force }
    $meta = [pscustomobject]@{ hadExisting=[bool]$had; target=$Target; createdAt=(Get-Date).ToString('o') }
    $meta | ConvertTo-Json | Set-Content -LiteralPath $metaPath -Encoding UTF8
    return $meta
}
function Install-Rose {
    Ensure-Closed
    $gta = Find-GtaLegacy
    $fiveM = Find-FiveMApp
    Cleanup-Old-Rose $gta

    $tools = Ensure-Tools
    $src = Prepare-PackageSource
    $mods = Join-Path $fiveM 'mods'
    New-Item -ItemType Directory -Force -Path $mods | Out-Null
    $target = Join-Path $mods 'Rose_Cinematic_Ultra.rpf'
    [void](Backup-Existing-Package $target)

    $tempRpf = Join-Path $BuildRoot 'Rose_Cinematic_Ultra.rpf'
    Remove-Item -LiteralPath $tempRpf -Force -ErrorAction SilentlyContinue
    Set-Status 'بناء Rose_Cinematic_Ultra.rpf الحقيقي لـ FiveM...'
    & $tools.DotNet $tools.Packer $src $tempRpf
    if ($LASTEXITCODE -ne 0) { throw ('فشل بناء RPF. ExitCode=' + $LASTEXITCODE) }
    if (-not (Test-Path -LiteralPath $tempRpf)) { throw 'ملف RPF لم يتم إنشاؤه.' }
    $bytes = [IO.File]::ReadAllBytes($tempRpf)
    if ($bytes.Length -lt 1024 -or [Text.Encoding]::ASCII.GetString($bytes,0,4) -ne 'RPF7') { throw 'Rose RPF غير صالح.' }

    Set-Status ('تركيب Rose داخل: ' + $mods)
    Copy-Item -LiteralPath $tempRpf -Destination $target -Force
    $hash = Get-Sha256 $target
    if (-not $hash -or $hash -ne (Get-Sha256 $tempRpf)) { throw 'فشل التحقق من Rose RPF بعد النسخ.' }

    $state = [pscustomobject]@{
        version=$RoseVersion; mode='FiveM-RPF'; installed=$true; installedAt=(Get-Date).ToString('o');
        gtaPath=$gta; fiveMApp=$fiveM; fiveMMods=$mods; packagePath=$target; packageHash=$hash;
        sourceGraphicsRef=$GraphicsRef; sourceMapRef=$MapRef
    }
    Save-State $state
    Set-Status 'Rose ACTIVE داخل FiveM.app\mods.'
    $nl = [Environment]::NewLine
    Show-Info ('تم تثبيت Rose بالطريقة الصحيحة لـ FiveM.' + $nl + $nl + 'Package: ' + $target + $nl + 'RPF7: True' + $nl + 'Map assets: ' + $MapFiles.Count + $nl + 'Graphics files: ' + $GraphicsFiles.Count + $nl + $nl + 'اقفل FiveM وافتحه من جديد.')
}
function Restore-Rose {
    Ensure-Closed
    $fiveM = Find-FiveMApp
    $target = Join-Path (Join-Path $fiveM 'mods') 'Rose_Cinematic_Ultra.rpf'
    $dir = Join-Path $Root 'backup\fivem-mods-v111'
    $metaPath = Join-Path $dir 'meta.json'
    if (Test-Path -LiteralPath $metaPath) {
        $meta = Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([bool]$meta.hadExisting -and (Test-Path -LiteralPath (Join-Path $dir 'Rose_Cinematic_Ultra.rpf'))) {
            Copy-Item -LiteralPath (Join-Path $dir 'Rose_Cinematic_Ultra.rpf') -Destination $target -Force
        } else {
            Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        }
    } else {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    }
    $s = Load-State
    if ($s) { $s.installed=$false; $s.restoredAt=(Get-Date).ToString('o'); Save-State $s }
    Set-Status 'تم استرجاع وضع FiveM قبل Rose.'
    Show-Info 'تم استرجاع/إزالة Rose من FiveM.app\mods.'
}
function Diagnose-Rose {
    try {
        $gta = Find-GtaLegacy
        $fiveM = Find-FiveMApp
        $target = Join-Path (Join-Path $fiveM 'mods') 'Rose_Cinematic_Ultra.rpf'
        $exists = Test-Path -LiteralPath $target
        $rpf7 = $false
        $hash = $null
        if ($exists) {
            $hash = Get-Sha256 $target
            $b = [IO.File]::ReadAllBytes($target)
            if ($b.Length -ge 4) { $rpf7 = ([Text.Encoding]::ASCII.GetString($b,0,4) -eq 'RPF7') }
        }
        $s = Load-State
        $hashOk = $false
        if ($s -and $s.packageHash -and $hash) { $hashOk = ($hash -eq ([string]$s.packageHash).ToUpperInvariant()) }
        $citizen = Join-Path $fiveM 'CitizenFX.ini'
        $iv = ''
        if (Test-Path -LiteralPath $citizen) {
            $line = Get-Content -LiteralPath $citizen -ErrorAction SilentlyContinue | Where-Object { $_ -like 'IVPath=*' } | Select-Object -First 1
            if ($line) { $iv = $line.Substring(7) }
        }
        $nl = [Environment]::NewLine
        Show-Info ('Rose v' + $RoseVersion + $nl + $nl +
            'FiveM.app: ' + $fiveM + $nl +
            'Package exists: ' + $exists + $nl +
            'RPF7 valid: ' + $rpf7 + $nl +
            'SHA verified: ' + $hashOk + $nl +
            'CitizenFX GTA path: ' + $iv + $nl +
            'Detected GTA path: ' + $gta + $nl + $nl +
            'Expected package:' + $nl + $target)
    } catch { Show-Error $_.Exception.Message }
}
function Check-Updates {
    $m = Get-RemoteManifest
    if (-not $m) { Show-Error 'تعذر الوصول إلى تحديثات Rose.'; return }
    Show-Info ('نسختك: ' + $RoseVersion + [Environment]::NewLine + 'أحدث نسخة: ' + [string]$m.version)
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Rose Cinematic Ultra'
$form.Size = New-Object System.Drawing.Size(760,430)
$form.StartPosition = 'CenterScreen'
$form.BackColor = [Drawing.Color]::FromArgb(24,24,24)
$form.ForeColor = [Drawing.Color]::White
$form.Font = New-Object Drawing.Font('Segoe UI',10)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false

$title = New-Object System.Windows.Forms.Label
$title.Text = 'ROSE CINEMATIC ULTRA'
$title.Font = New-Object Drawing.Font('Segoe UI',20,[Drawing.FontStyle]::Bold)
$title.AutoSize = $true
$title.Location = New-Object Drawing.Point(24,22)
$form.Controls.Add($title)

$sub = New-Object System.Windows.Forms.Label
$sub.Text = 'v1.0.11 - FiveM RPF package mode'
$sub.ForeColor = [Drawing.Color]::Silver
$sub.AutoSize = $true
$sub.Location = New-Object Drawing.Point(28,66)
$form.Controls.Add($sub)

$desc = New-Object System.Windows.Forms.Label
$desc.Text = 'التثبيت الصحيح: %LOCALAPPDATA%\FiveM\FiveM.app\mods\Rose_Cinematic_Ultra.rpf'
$desc.AutoSize = $true
$desc.Location = New-Object Drawing.Point(28,96)
$form.Controls.Add($desc)

$script:StatusLabel = New-Object System.Windows.Forms.Label
$script:StatusLabel.Text = 'جاهز. ملفات GTA الأصلية لن يتم تعديلها.'
$script:StatusLabel.AutoSize = $false
$script:StatusLabel.Size = New-Object Drawing.Size(690,50)
$script:StatusLabel.Location = New-Object Drawing.Point(28,130)
$script:StatusLabel.ForeColor = [Drawing.Color]::Gainsboro
$form.Controls.Add($script:StatusLabel)

function Add-Button([string]$Text,[int]$X,[int]$Y,[int]$W,[scriptblock]$Action) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $Text
    $b.Size = New-Object Drawing.Size($W,54)
    $b.Location = New-Object Drawing.Point($X,$Y)
    $b.BackColor = [Drawing.Color]::FromArgb(46,46,46)
    $b.ForeColor = [Drawing.Color]::White
    $b.FlatStyle = 'Flat'
    $b.Add_Click($Action)
    $form.Controls.Add($b)
}
Add-Button 'تثبيت / إصلاح Rose' 28 190 325 {
    try { Install-Rose } catch { Show-Error ($_.Exception.Message + [Environment]::NewLine + [Environment]::NewLine + 'Log: ' + $LogPath) }
}
Add-Button 'تشخيص التثبيت' 377 190 325 { Diagnose-Rose }
Add-Button 'استرجاع قبل Rose' 28 260 325 {
    try { Restore-Rose } catch { Show-Error $_.Exception.Message }
}
Add-Button 'فحص تحديث' 377 260 325 { Check-Updates }

$foot = New-Object System.Windows.Forms.Label
$foot.Text = 'FiveM client package. No DLL/ASI injection. Server pure mode can block client mods.'
$foot.ForeColor = [Drawing.Color]::Gray
$foot.AutoSize = $true
$foot.Location = New-Object Drawing.Point(28,340)
$form.Controls.Add($foot)

Write-Log ('Rose v' + $RoseVersion + ' started.')
[void]$form.ShowDialog()
