<#
.SYNOPSIS
    DREX PC Cleaner Performance Measurement Script
.DESCRIPTION
    Measures memory, CPU, handles, threads, and child processes of DREX PC Cleaner
    with jittered sampling intervals to avoid phase-locking with application timers.
    Outputs CSV with per-sample data for trend analysis.
.NOTES
    Based on recommendations from performance assessment:
    - Jittered sampling (1-5s) to avoid phase-locking with 5s app timer
    - Fractional CPU calculation
    - Child process monitoring (PowerShell, etc.)
    - GDI/USER object tracking via Get-Process
    - Per-sample CSV for trend analysis
#>

param(
    [string]$ProcessName = "DREX_PC_CLEANER",
    [string]$OutputPath = "$env:USERPROFILE\Desktop\drex_measurement.csv",
    [int]$SampleIntervalMs = 1000,
    [int]$MaxJitterMs = 4000,
    [switch]$IncludeGC = $false
)

# Get logical processor count for CPU normalization
$coreCount = [Environment]::ProcessorCount

# Initialize tracking variables
$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $process) {
    Write-Error "Process '$ProcessName' not found. Start DREX PC Cleaner first."
    exit 1
}

$lastCpuTime = $process.TotalProcessorTime
$lastTime = Get-Date

# CSV header
$header = "Time,WS_MB,Private_MB,Handles,Threads,CPU_Pct,Children,GDI_Objects,USER_Objects,GC_Gen0,GC_Gen1,GC_Gen2,GC_LOH_MB"
$header | Out-File -FilePath $OutputPath -Encoding utf8

Write-Host "Started measuring '$ProcessName' (PID: $($process.Id))"
Write-Host "Core count: $coreCount"
Write-Host "Output: $OutputPath"
Write-Host "Press Ctrl+C to stop and finalize CSV"
Write-Host ""

try {
    while ($true) {
        # Jittered sleep: base interval + random jitter (1-5s total)
        $sleepMs = $SampleIntervalMs + (Get-Random -Minimum 0 -Maximum $MaxJitterMs)
        Start-Sleep -Milliseconds $sleepMs

        # Re-get process (handles restarts)
        $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
        if (-not $process) {
            Write-Warning "Process '$ProcessName' not found. Waiting..."
            continue
        }

        $now = Get-Date
        $elapsedSec = ($now - $lastTime).TotalSeconds

        # CPU calculation: (delta CPU time) / (elapsed wall time * core count) * 100
        $currentCpuTime = $process.TotalProcessorTime
        $cpuDeltaMs = ($currentCpuTime - $lastCpuTime).TotalMilliseconds
        $cpuPct = if ($elapsedSec > 0) { [math]::Round(($cpuDeltaMs / 1000) / ($elapsedSec * $coreCount) * 100, 3) } else { 0 }

        # Memory in MB
        $wsMb = [math]::Round($process.WorkingSet64 / 1MB, 2)
        $privateMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)

        # Handles and threads
        $handles = $process.HandleCount
        $threads = $process.Threads.Count

        # Child processes
        $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" -ErrorAction SilentlyContinue).Count

        # GDI and USER objects (from Get-Process)
        $gdi = $process.GDIObjects
        $user = $process.UserObjects

        # GC info (if requested and available)
        $gcGen0 = $gcGen1 = $gcGen2 = $gcLohMb = ""
        if ($IncludeGC) {
            try {
                # Use dotnet-counters if available for managed GC stats
                # For now, leave empty - would need dotnet-counters integration
            } catch { }
        }

        # Build CSV row
        $row = @(
            $now.ToString('o'),
            $wsMb,
            $privateMb,
            $handles,
            $threads,
            $cpuPct,
            $children,
            $gdi,
            $user,
            $gcGen0,
            $gcGen1,
            $gcGen2,
            $gcLohMb
        ) -join ','

        $row | Out-File -FilePath $OutputPath -Encoding utf8 -Append

        # Console output (compact)
        Write-Host "[$($now.ToString('HH:mm:ss'))] WS: $wsMb MB | Priv: $privateMb MB | Handles: $handles | Threads: $threads | CPU: $cpuPct% | Children: $children | GDI: $gdi | USER: $user"

        # Update tracking
        $lastCpuTime = $currentCpuTime
        $lastTime = $now
    }
}
finally {
    Write-Host ""
    Write-Host "Measurement stopped. CSV saved to: $OutputPath"
}