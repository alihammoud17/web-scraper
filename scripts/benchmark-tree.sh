#!/usr/bin/env bash
# Benchmark: crawl the 585-page TestSite tree (branch 8, depth 3) with 4 workers. TestSite allows 5 req/s, so the
# shared rate limit, not the worker count, sets the pace (about 2 minutes).
# Expected: 585 pages, every task succeeded, no duplicate URLs, tasks or fetches.
source "$(dirname "$0")/lib.sh"
trap 'restore_workers 2' EXIT

say "Setup: 4 workers"
ensure_stack
workers 4
key="b$(date +%s)"

say "Crawl /tree/$key (585 pages)"
job=$(start_crawl "http://testsite:8080/tree/$key")
started=$(date +%s)
wait_for_job "$job" 600
elapsed=$(( $(date +%s) - started ))
check "job status" "$(job_status "$job")" "Completed"
check "tasks" "$(sql "select count(*) from crawl_tasks where crawl_job_id = '$job'")" "585"
check "tasks succeeded" "$(sql "select count(*) from crawl_tasks where crawl_job_id = '$job' and status = 'Succeeded'")" "585"
check "duplicate URLs in the job" "$(sql "select count(*) - count(distinct url) from crawl_tasks where crawl_job_id = '$job'")" "0"
check "fetch rows (one per task)" "$(sql "select count(*) from page_fetches f join crawl_tasks t on t.id = f.crawl_task_id where t.crawl_job_id = '$job'")" "585"
info "$elapsed s for 585 pages = $(python3 -c "print(f'{585 / max($elapsed, 1):.2f}')") pages/s (site limit: 5 req/s)"
info "tasks per worker: $(docker compose logs worker 2>&1 | grep "Processing task .*/tree/$key" | cut -d' ' -f1 | sort | uniq -c | tr '\n' ' ')"
finish
