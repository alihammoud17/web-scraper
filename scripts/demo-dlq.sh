#!/usr/bin/env bash
# Scenario: a URL that always fails (HTTP 500). Retry tiers shortened to 2 s, 4 s, 8 s and 4 attempts for the demo.
# Expected: retried through the delay tiers, then dead-lettered with its last error and attempt count. After the
# page is fixed, POST /dead-letters/{taskId}/retry re-queues it and it succeeds.
source "$(dirname "$0")/lib.sh"
trap 'restore_workers 2' EXIT

say "Setup: 2 workers, 4 attempts, retry tiers 2 s, 4 s, 8 s"
ensure_stack
workers 2 QUEUE_MAX_ATTEMPTS=4 QUEUE_RETRY_DELAYS=2,4,8
key="d$(date +%s)"

say "/fail/$key always answers 500"
job=$(start_crawl "http://testsite:8080/fail/$key")
started=$(date +%s)
wait_for_job "$job" 120
task=$(sql "select id from crawl_tasks where crawl_job_id = '$job'")
info "dead-lettered after $(( $(date +%s) - started )) s; request times (s): $(hit_times "/fail/$key")"
check "task status" "$(sql "select status from crawl_tasks where id = '$task'")" "DeadLettered"
check "dead-letter entry attempt count" "$(sql "select attempt_count from dead_letter_entries where crawl_task_id = '$task'")" "4"
check "dead-letter last error" "$(sql "select last_error from dead_letter_entries where crawl_task_id = '$task'")" "gave up after 4 attempts: HTTP 500"
check "requests (one per attempt; 500 isn't retried in-process)" "$(hit_times "/fail/$key" | wc -w | tr -d ' ')" "4"
check "job completed (nothing left to do)" "$(job_status "$job")" "Completed"

say "Fix the page, then retry the dead letter through the API"
curl -sf -X POST "localhost:8090/fail/$key/heal" >/dev/null
check "POST /dead-letters/{taskId}/retry" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "localhost:8080/dead-letters/$task/retry")" "202"
wait_for_job "$job" 60
check "task status after retry" "$(sql "select status from crawl_tasks where id = '$task'")" "Succeeded"
check "dead-letter entry marked retried" "$(sql "select (retried_at is not null)::int from dead_letter_entries where crawl_task_id = '$task'")" "1"
check "job completed again" "$(job_status "$job")" "Completed"
finish
