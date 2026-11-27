#!/usr/bin/env bash
# Scenario: network timeouts. Per-attempt timeout shortened to 2 s for the demo.
# Expected: (1) a URL that hangs twice then answers succeeds within one task attempt (in-process retries with
# backoff and jitter); (2) a URL that always hangs is retried through the queue's delay tier, then dead-lettered.
source "$(dirname "$0")/lib.sh"
trap 'restore_workers 2' EXIT

say "Setup: 2 workers, 2 s attempt timeout, 2 queue attempts, 2 s retry tier"
ensure_stack
workers 2 CRAWLER_ATTEMPT_TIMEOUT_SECONDS=2 QUEUE_MAX_ATTEMPTS=2 QUEUE_RETRY_DELAYS=2
key="t$(date +%s)"

say "1. /hang/${key}a hangs for the first 2 requests, then answers"
job=$(start_crawl "http://testsite:8080/hang/${key}a?hangs=2")
wait_for_job "$job" 120
check "task status" "$(sql "select status from crawl_tasks where crawl_job_id = '$job'")" "Succeeded"
check "task attempts (all retries inside one attempt)" "$(sql "select attempt_count from crawl_tasks where crawl_job_id = '$job'")" "1"
check "requests seen by TestSite (2 timed out + 1 answered)" "$(hit_times "/hang/${key}a" | wc -w | tr -d ' ')" "3"
info "request times (s): $(hit_times "/hang/${key}a")"

say "2. /hang/${key}b always hangs"
job=$(start_crawl "http://testsite:8080/hang/${key}b?hangs=99")
wait_for_job "$job" 180
task=$(sql "select id from crawl_tasks where crawl_job_id = '$job'")
check "task status" "$(sql "select status from crawl_tasks where id = '$task'")" "DeadLettered"
check "dead-letter entry attempt count" "$(sql "select attempt_count from dead_letter_entries where crawl_task_id = '$task'")" "2"
check "dead-letter reason mentions a timeout" "$(sql "select (last_error ilike '%timeout%')::int from dead_letter_entries where crawl_task_id = '$task'")" "1"
check "requests (2 queue attempts × 4 timed-out tries)" "$(hit_times "/hang/${key}b" | wc -w | tr -d ' ')" "8"
info "reason: $(sql "select last_error from dead_letter_entries where crawl_task_id = '$task'")"
finish
