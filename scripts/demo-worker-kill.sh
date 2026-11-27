#!/usr/bin/env bash
# Scenario: a worker is killed (SIGKILL, like a crashed container) in the middle of a task.
# Expected: the broker redelivers the unacked task to another worker; the crawl completes with every page
# fetched and stored exactly once (nothing lost, nothing duplicated). Part 2: a graceful stop (SIGTERM)
# finishes and acks the in-flight task instead, so it is not processed again.
source "$(dirname "$0")/lib.sh"
trap 'restore_workers 2' EXIT

say "Setup: 3 workers, a 13-page TestSite tree where every page takes 2 s to answer"
ensure_stack
workers 3
key="k$(date +%s)"
job=$(start_crawl "http://testsite:8080/delay/2000/tree/$key?branch=3&depth=2")
info "job $job"

say "Kill the worker that is processing a task"
wait_until "a task in progress" 60 "[ -n \"\$(sql \"select id from crawl_tasks where crawl_job_id = '$job' and status = 'InProgress' limit 1\")\" ]"
task=$(sql "select id from crawl_tasks where crawl_job_id = '$job' and status = 'InProgress' limit 1")
victim=$(worker_processing "$task")
info "task $task is in progress on container ${victim:0:12}; docker kill (SIGKILL)"
docker kill "$victim" >/dev/null

say "Wait for the crawl to finish on the remaining workers"
wait_for_job "$job" 180
check "job status" "$(job_status "$job")" "Completed"
check "tasks (1 + 3 + 9 tree pages)" "$(sql "select count(*) from crawl_tasks where crawl_job_id = '$job'")" "13"
check "tasks succeeded" "$(sql "select count(*) from crawl_tasks where crawl_job_id = '$job' and status = 'Succeeded'")" "13"
check "fetch rows (one per task, none duplicated)" "$(sql "select count(*) from page_fetches f join crawl_tasks t on t.id = f.crawl_task_id where t.crawl_job_id = '$job'")" "13"
check "duplicate URLs in the job" "$(sql "select count(*) - count(distinct url) from crawl_tasks where crawl_job_id = '$job'")" "0"
check "killed task was started again (attempt count ≥ 2)" "$(sql "select attempt_count >= 2 from crawl_tasks where id = '$task'")" "t"
redelivered=$(docker compose logs worker 2>&1 | grep -c "Processing task $task .*redelivered=True" || true)
check "another worker received it with redelivered=True" "$(( redelivered >= 1 ))" "1"

say "Part 2: graceful stop (SIGTERM) of a worker in the middle of a task"
workers 3
key="g$(date +%s)"
job=$(start_crawl "http://testsite:8080/delay/3000/tree/$key?branch=3&depth=1")
wait_until "a task in progress" 60 "[ -n \"\$(sql \"select id from crawl_tasks where crawl_job_id = '$job' and status = 'InProgress' limit 1\")\" ]"
task=$(sql "select id from crawl_tasks where crawl_job_id = '$job' and status = 'InProgress' limit 1")
stopping=$(worker_processing "$task")
info "task $task is in progress on container ${stopping:0:12}; docker stop (SIGTERM)"
docker stop "$stopping" >/dev/null
wait_for_job "$job" 120
check "the stopping worker finished and acked its task" "$(docker logs "$stopping" 2>&1 | grep -c 'Finished in-flight task, acked')" "1"
check "that task ran exactly once (attempt count)" "$(sql "select attempt_count from crawl_tasks where id = '$task'")" "1"
check "job status" "$(job_status "$job")" "Completed"
check "tasks succeeded (1 + 3)" "$(sql "select count(*) from crawl_tasks where crawl_job_id = '$job' and status = 'Succeeded'")" "4"
finish
