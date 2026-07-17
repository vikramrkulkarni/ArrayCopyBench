#!/usr/bin/env bash
#
# run-manual.sh  (Ubuntu / Linux)
# Runs ArrayCopyManual.dll for a set of array lengths and prints a summary table.
# Assumes ArrayCopyManual.dll is in the SAME folder as this script.
#
# Usage:
#   chmod +x run-manual.sh
#   ./run-manual.sh          # default 7 timed runs per length
#   ./run-manual.sh 5        # 5 timed runs per length
#
# Requires the .NET 8 runtime (dotnet) on PATH.

set -euo pipefail

RUNS="${1:-7}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DLL="$SCRIPT_DIR/ArrayCopyManual.dll"

if [[ ! -f "$DLL" ]]; then
    echo "ERROR: ArrayCopyManual.dll not found next to this script: $DLL" >&2
    exit 1
fi

# Lengths to test, and iterations per length (fewer for larger copies to keep runtime
# short; ns/copy is normalized by iterations, so the numbers stay comparable).
lengths=(4        10        100       500      2000)
iters=(  50000000 50000000  20000000  5000000  2000000)

CSV="$SCRIPT_DIR/arraycopymanual-results.csv"
echo "Length,Iterations,Best_ms,ns_per_copy" > "$CSV"

# Collect rows for the summary table.
declare -a rows=()

echo "Using: $DLL"

for i in "${!lengths[@]}"; do
    len="${lengths[$i]}"
    it="${iters[$i]}"
    echo ""
    echo "Running length=$len  iterations=$it  runs=$RUNS ..."

    out="$(dotnet "$DLL" --length "$len" --iterations "$it" --runs "$RUNS" 2>&1)"

    line="$(echo "$out" | grep 'ns/copy' | head -n1 || true)"
    if [[ -z "$line" ]]; then
        echo "  (no result parsed - raw output below)"
        echo "$out" | sed 's/^/    /'
        continue
    fi

    echo "  $line"
    ns="$(echo "$line"   | sed -E 's/.*\(([0-9.]+) ns\/copy\).*/\1/')"
    best="$(echo "$line" | sed -E 's/.*best = *([0-9.]+) ms.*/\1/')"

    printf "%s,%s,%s,%s\n" "$len" "$it" "$best" "$ns" >> "$CSV"
    rows+=("$(printf '%-8s %-12s %-10s %-10s' "$len" "$it" "$best" "$ns")")
done

echo ""
echo "===== SUMMARY (ArrayCopyManual) ====="
printf "%-8s %-12s %-10s %-10s\n" "Length" "Iterations" "Best(ms)" "ns/copy"
printf '%s\n' "${rows[@]}"
echo ""
echo "Saved: $CSV"
