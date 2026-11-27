#!/usr/bin/env bash
# Proves the shared rate limit from TestSite's request log: every request is logged as
# "HIT <UTC time> <path> <user-agent>", whichever worker sent it.
# Usage: tools/check-rate.sh [expected requests per second, default 1]
set -euo pipefail
limit="${1:-1}"

docker compose logs --no-log-prefix testsite | grep '^HIT ' | python3 -c '
import sys
from datetime import datetime

limit = float(sys.argv[1])
times = sorted(datetime.fromisoformat(line.split()[1]) for line in sys.stdin)
if len(times) < 2:
    sys.exit("Fewer than 2 requests in the TestSite log; is the probe running?")

seconds = [(t - times[0]).total_seconds() for t in times]
gaps = [b - a for a, b in zip(seconds, seconds[1:])]
span = seconds[-1]
# Most requests inside any 1-second sliding window.
window_max = max(sum(1 for s in seconds[i:] if s - start < 1.0) for i, start in enumerate(seconds))

print(f"requests:            {len(times)}")
print(f"time span:           {span:.1f} s")
print(f"average rate:        {(len(times) - 1) / span:.3f} req/s (limit {limit:g})")
print(f"smallest gap:        {min(gaps):.3f} s (expected about {1 / limit:.3f} s)")
print(f"max in any 1 s:      {window_max}")

# Small network jitter is allowed: each gap must be at least 90 % of 1 / limit.
ok = min(gaps) >= 0.9 / limit and (len(times) - 1) / span <= limit * 1.02
print("RESULT:              " + ("PASS (all workers together stayed within the limit)" if ok else "FAIL"))
sys.exit(0 if ok else 1)
' "$limit"
