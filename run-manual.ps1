<#
    run-manual.ps1
    Runs the ArrayCopyManual benchmark for a set of array lengths and prints a summary table.

    Usage (from the D:\TrialCode\ArrayCopyBench folder):
        powershell -ExecutionPolicy Bypass -File .\run-manual.ps1

    Optional:
        .\run-manual.ps1 -Runs 7        # timed runs per length (default 7)
#>
param(
    [int]$Runs = 7
)

$ErrorActionPreference = 'Stop'

# Lengths to test, and iterations per length (fewer for larger copies to keep runtime short;
# ns/copy is normalized by iterations, so the numbers stay comparable).
# NOTE: use an array of objects, not an [ordered] hashtable — integer indexing on an
# OrderedDictionary is treated as a positional index, not a key lookup.
$plan = @(
    [pscustomobject]@{ Length = 4;    Iters = 50000000 }
    [pscustomobject]@{ Length = 10;   Iters = 50000000 }
    [pscustomobject]@{ Length = 100;  Iters = 20000000 }
    [pscustomobject]@{ Length = 500;  Iters = 5000000  }
    [pscustomobject]@{ Length = 2000; Iters = 2000000  }
)

$exe = Join-Path $PSScriptRoot 'ArrayCopyManual\bin\Release\net8.0\ArrayCopyManual.exe'
if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe`nBuild it once first: dotnet build .\ArrayCopyManual\ArrayCopyManual.csproj -c Release"
}
Write-Host "Using: $exe" -ForegroundColor Cyan

$results = New-Object System.Collections.Generic.List[object]

foreach ($p in $plan) {
    $len   = $p.Length
    $iters = $p.Iters
    Write-Host ""
    Write-Host ("Running length={0}  iterations={1:N0}  runs={2} ..." -f $len, $iters, $Runs) -ForegroundColor Cyan

    $out = & $exe --length $len --iterations $iters --runs $Runs 2>&1
    $line = $out | Select-String 'ns/copy' | Select-Object -First 1

    if ($null -eq $line) {
        Write-Host "  (no result parsed - raw output below)" -ForegroundColor Yellow
        $out | ForEach-Object { Write-Host "    $_" }
        continue
    }

    $text    = $line.ToString()
    $nsCopy  = [double]($text -replace '.*\(([\d.]+)\s*ns/copy\).*', '$1')
    $bestMs  = [double]($text -replace '.*best\s*=\s*([\d.]+)\s*ms.*', '$1')

    Write-Host ("  {0}" -f $text)

    $results.Add([pscustomobject]@{
        Length       = $len
        Iterations   = $iters
        'Best (ms)'  = [math]::Round($bestMs, 2)
        'ns/copy'    = [math]::Round($nsCopy, 3)
    })
}

Write-Host ""
Write-Host "===== SUMMARY (ArrayCopyManual) =====" -ForegroundColor Green
$results | Format-Table -AutoSize

# Optional: save a CSV next to this script for pasting into the report
$csv = Join-Path $PSScriptRoot 'arraycopymanual-results.csv'
$results | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
Write-Host "Saved: $csv" -ForegroundColor DarkGray
