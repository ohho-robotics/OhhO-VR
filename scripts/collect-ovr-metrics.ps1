# Collect OVR Metrics Tool CSVs from a Quest on USB.
# This script does not launch the player and does not enable the on-device overlay.
# It was not run when OHH-231 was written. There is no headset in that job.
# Procedure and column names: docs/performance.md
# Commands follow Meta's OVR Metrics Tool page (report mode / ENABLE_CSV).

param(
    [ValidateSet('Enable', 'Pull', 'Stop')]
    [string]$Action = 'Enable',

    [string]$Serial = '',

    [string]$OutDir = '',

    [int]$Seconds = 0
)

$ErrorActionPreference = 'Stop'

$Receiver = 'com.oculus.ovrmonitormetricsservice/.SettingsBroadcastReceiver'
$CsvDir = '/sdcard/Android/data/com.oculus.ovrmonitormetricsservice/files/CapturedMetrics'

# Stats named by Meta's tool. ENABLE_CSV records metrics; these broadcasts select
# the columns the budget table needs so report mode is not left on an empty preset.
$Stats = @(
    'average_frame_rate',
    'display_refresh_rate',
    'app_gpu_time_microseconds',
    'cpu_utilization_percentage',
    'gpu_utilization_percentage',
    'cpu_level',
    'gpu_level',
    'foveation_level',
    'stale_frame_count',
    'early_frame_count',
    'battery_temperature_celcius',
    'sensor_temperature_celcius',
    'power_level_state',
    'eye_buffer_width',
    'eye_buffer_height',
    'app_pss_MB',
    'cpu_utilization_percentage_core0',
    'cpu_utilization_percentage_core1',
    'cpu_utilization_percentage_core2',
    'cpu_utilization_percentage_core3',
    'cpu_utilization_percentage_core4',
    'cpu_utilization_percentage_core5',
    'cpu_utilization_percentage_core6',
    'cpu_utilization_percentage_core7'
)

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$AdbArgs)
    if ($Serial) {
        & adb -s $Serial @AdbArgs
    } else {
        & adb @AdbArgs
    }
    if ($LASTEXITCODE -ne 0) {
        throw "adb exited $LASTEXITCODE : adb $($AdbArgs -join ' ')"
    }
}

function Assert-OneDevice {
    $lines = & adb devices
    if ($LASTEXITCODE -ne 0) {
        throw 'adb devices failed. Install platform-tools and connect a Quest.'
    }
    $ready = @()
    foreach ($line in $lines) {
        if ($line -match '^\s*(\S+)\s+device\s*$') {
            $ready += $Matches[1]
        }
    }
    if ($Serial) {
        if ($ready -notcontains $Serial) {
            throw "Serial $Serial is not in 'adb devices' as a ready device."
        }
        return
    }
    if ($ready.Count -eq 0) {
        throw 'No headset in adb devices. Connect a Quest and accept the USB prompt. This script does not talk to a robot.'
    }
    if ($ready.Count -gt 1) {
        throw "More than one device ($($ready -join ', ')). Re-run with -Serial."
    }
}

function Send-Broadcast {
    param([string]$Name, [string]$Stat)
    $adbArgs = @('shell', 'am', 'broadcast', '-n', $Receiver, '-a', "com.oculus.ovrmonitormetricsservice.$Name")
    if ($Stat) {
        $adbArgs += @('--es', 'stat', $Stat)
    }
    Write-Output ("broadcast " + $Name + $(if ($Stat) { " stat=$Stat" } else { '' }))
    Invoke-Adb @adbArgs | Out-Null
}

if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
    throw 'adb is not on PATH.'
}

Assert-OneDevice

switch ($Action) {
    'Enable' {
        Send-Broadcast 'ENABLE_CSV'
        foreach ($stat in $Stats) {
            Send-Broadcast 'ENABLE_STAT' $stat
        }
        Write-Output 'CSV recording requested. The performance HUD overlay was not enabled.'
        Write-Output 'Start the Development Build on the headset yourself.'
        if ($Seconds -gt 0) {
            Write-Output "Sleeping $Seconds seconds. Start the scenario now. This does not confirm the app is running."
            try {
                Start-Sleep -Seconds $Seconds
                if (-not $OutDir) {
                    $OutDir = Join-Path $env:USERPROFILE 'ohho\logs\ohh-231-metrics'
                }
                New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
                Write-Output "Pulling $CsvDir -> $OutDir"
                Invoke-Adb pull $CsvDir $OutDir
            } finally {
                Send-Broadcast 'DISABLE_CSV'
                Write-Output 'CSV recording disabled. Read the files before filling docs/performance.md.'
            }
        }
    }
    'Pull' {
        if (-not $OutDir) {
            $OutDir = Join-Path $env:USERPROFILE 'ohho\logs\ohh-231-metrics'
        }
        New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
        Write-Output "Pulling $CsvDir -> $OutDir"
        Invoke-Adb pull $CsvDir $OutDir
        Write-Output 'Pull finished. This script does not score the CSV.'
    }
    'Stop' {
        Send-Broadcast 'DISABLE_CSV'
        Write-Output 'CSV recording disabled.'
    }
}
