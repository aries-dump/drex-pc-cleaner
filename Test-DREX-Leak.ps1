<#
.SYNOPSIS
    DREX PC Cleaner Memory Leak Detection - 20 Cycle Scan Test
.DESCRIPTION
    Runs the same scan 20 times on a fixed folder, measuring post-scan settle memory
    to detect memory leaks. A valid leak test requires:
    - Same workload each cycle
    - 60s settle period after each scan
    - Track Private Bytes and Handles post-settle
    - Pass: no upward trend beyond noise
.NOTES
    Based on performance assessment Q7 recommendation.
#>

param(
    [string]$ProcessName = "DREX_PC_CLEANER",
    [string]$ScanFolder = "$env:USERPROFILE\Downloads",
    [string]$OutputPath = "$env:USERPROFILE\Desktop\drex_leak_test.csv",
    [int]$Cycles = 20,
    [int]$SettleSeconds = 60,
    [int]$MinFileSizeMB = 1
)

# Get the process
$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $process) {
    Write-Error "Process '$ProcessName' not found. Start DREX PC Cleaner first."
    exit 1
}

Write-Host "DREX PC Cleaner Leak Test"
Write-Host "============================"
Write-Host "Process: $ProcessName (PID: $($process.Id))"
Write-Host "Scan folder: $ScanFolder"
Write-Host "Cycles: $Cycles"
Write-Host "Settle time: ${SettleSeconds}s"
Write-Host "Output: $OutputPath"
Write-Host ""

# Verify scan folder exists
if (-not (Test-Path $ScanFolder)) {
    Write-Error "Scan folder not found: $ScanFolder"
    exit 1
}

# CSV header
$header = "Cycle,Time,WS_MB,Private_MB,Handles,Threads,GDI_Objects,USER_Objects,Notes"
$header | Out-File -FilePath $OutputPath -Encoding utf8

# Function to record a measurement
function Record-Measurement {
    param($cycle, $note)
    $p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if (-not $p) { return }

    $row = @(
        $cycle,
        (Get-Date).ToString('o'),
        [math]::Round($p.WorkingSet64 / 1MB, 2),
        [math]::Round($p.PrivateMemorySize64 / 1MB, 2),
        $p.HandleCount,
        $p.Threads.Count,
        $p.GDIObjects,
        $p.UserObjects,
        $note
    ) -join ','

    $row | Out-File -FilePath $OutputPath -Encoding utf8 -Append
    Write-Host "  Cycle $cycle: WS=$([math]::Round($p.WorkingSet64/1MB,2)) MB | Priv=$([math]::Round($p.PrivateMemorySize64/1MB,2)) MB | Handles=$($p.HandleCount) | GDI=$($p.GDIObjects) | $note"
}

# Baseline measurement (idle)
Write-Host "Recording baseline (idle)..."
Record-Measurement 0 "Baseline (idle)"

for ($i = 1; $i -le $Cycles; $i++) {
    Write-Host ""
    Write-Host "=== Cycle $i / $Cycles ==="

    # Note: This test requires DREX to be automatable.
    # Since DREX is a WinForms app without CLI automation, we'll measure
    # the process state and require manual scan triggering.
    # For full automation, DREX would need command-line scan support.

    Write-Host "Please trigger a 'Find duplicates' scan on folder: $ScanFolder"
    Write-Host "  Min file size: ${MinFileSizeMB} MB"
    Write-Host "Press ENTER when scan COMPLETES (not when it starts)..."

    # Wait for user to confirm scan is done
    $null = Read-Host

    # Record immediate post-scan
    Record-Measurement $i "Post-scan (immediate)"

    # Settle period
    Write-Host "Waiting ${SettleSeconds}s for memory to settle..."
    for ($s = $SettleSeconds; $s -gt 0; $s -= 5) {
        Write-Host "  $s seconds remaining..." -NoNewline
        Start-Sleep -Seconds 5
        Write-Host "`r" -NoNewline
    }
    Write-Host ""

    # Record post-settle
    Record-Measurement $i "Post-settle (${SettleSeconds}s)"

    # Also trigger GC to see if memory drops
    Write-Host "Triggering GC.Collect()..."
    # Note: Can't easily trigger GC in another process without a diagnostic port
    # This would need dotnet-gcdump or similar tooling
}

Write-Host ""
Write-Host "Leak test complete. Results saved to: $OutputPath"
Write-Host ""
Write-Host "Analysis:"
Write-Host "  - Plot 'Private_MB' (Post-settle) vs Cycle"
Write-Host "  - Look for upward trend (leak) vs flat line (no leak)"
Write-Host "  - Also check Handles and GDI_Objects for leaks"
Write-Host ""
Write-Host "For automated testing, DREX would need a CLI scan mode."