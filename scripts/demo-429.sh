#!/usr/bin/env bash
# Scenario: the site answers 429 Too Many Requests with Retry-After: 3.
# Expected: the crawler waits at least 3 s before each retry (Retry-After honoured), then succeeds.
source "$(dirname "$0")/lib.sh"
trap 'restore_workers 2' EXIT

say "Setup: 2 workers with default settings"
ensure_stack
workers 2
key="r$(date +%s)"

say "/flaky/$key answers 429 (Retry-After: 3) twice, then 200"
job=$(start_crawl "http://testsite:8080/flaky/$key?status=429&failures=2&retryAfter=3")
wait_for_job "$job" 120
times=$(hit_times "/flaky/$key")
info "request times (s): $times"
check "task status" "$(sql "select status from crawl_tasks where crawl_job_id = '$job'")" "Succeeded"
check "requests seen by TestSite (429, 429, 200)" "$(echo "$times" | wc -w | tr -d ' ')" "3"
gaps_ok=$(python3 -c "t=[float(x) for x in '$times'.split()]; print(int(all(b - a >= 2.9 for a, b in zip(t, t[1:]))))")
check "every retry waited ≥ 3 s (Retry-After honoured)" "$gaps_ok" "1"
check "fetch row stored with status 200" "$(sql "select f.status_code from page_fetches f join crawl_tasks t on t.id = f.crawl_task_id where t.crawl_job_id = '$job'")" "200"
finish
