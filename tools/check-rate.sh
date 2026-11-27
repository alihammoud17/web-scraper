#!/usr/bin/env bash
# Proves the shared rate limit from TestSite's request log: every request is logged as
# "HIT <UTC time> <path> <user-agent>", whichever worker sent it. Only requests carrying the crawler's
# User-Agent count (manual curl requests to TestSite aren't the crawler's). For a clean measurement, start
# from a fresh log: docker compose up -d --force-recreate testsite
# Usage: tools/check-rate.sh [expected requests per second, default 5 (TestSite's seeded limit)]
set -euo pipefail
cd "$(dirname "$0")/.."
limit="${1:-5}"
bot=$(grep '^CRAWLER_USER_AGENT=' .env | cut -d= -f2- | cut -d/ -f1)

docker compose logs --no-log-prefix testsite | grep '^HIT ' | grep -F -- "$bot" | python3 -c '
import sys
from datetime import datetime

limit = float(sys.argv[1])
times = sorted(datetime.fromisoformat(line.split()[1]) for line in sys.stdin)
if len(times) < 2:
    sys.exit("Fewer than 2 requests in the TestSite log; is the probe running?")

seconds = [(t - times[0]).total_seconds() for t in times]
gaps = [b - a for a, b in zip(seconds, seconds[1:])]
span = seconds[-1]
average = (len(times) - 1) / span

def busiest(window):
    """Most requests inside any sliding window of the given length (seconds)."""
    return max(sum(1 for s in seconds[i:] if s - start < window) for i, start in enumerate(seconds))

# A token bucket with capacity 1 guarantees at most floor(rate x W) + 1 requests in any W-second window. Gaps between
# single requests can be a little shorter than 1/rate: tokens are granted on time by Redis, but each request then
# needs a few ms (varying per worker) to reach the site.
checks = []
for window in (1, 10):
    allowed = int(limit * window) + 1
    seen = busiest(window)
    checks.append(seen <= allowed)
    print(f"max in any {window:>2} s:     {seen} (allowed {allowed})")

print(f"requests:            {len(times)}")
print(f"time span:           {span:.1f} s")
print(f"average rate:        {average:.3f} req/s (limit {limit:g})")
print(f"smallest gap:        {min(gaps):.3f} s (nominal {1 / limit:.3f} s; a few ms of network jitter is normal)")

ok = all(checks) and average <= limit * 1.02
print("RESULT:              " + ("PASS (all workers together stayed within the limit)" if ok else "FAIL"))
sys.exit(0 if ok else 1)
' "$limit"
