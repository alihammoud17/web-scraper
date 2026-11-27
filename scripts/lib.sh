#!/usr/bin/env bash
# Shared helpers for the demo scripts. Run the scripts from anywhere; they work from the repository root and
# need the stack built once (docker compose up --build -d) and a .env file.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

FAILURES=0
STARTED_AT=$(date +%s)

env_value() { grep "^$1=" .env | cut -d= -f2-; }
PG_USER=$(env_value POSTGRES_USER)
PG_DB=$(env_value POSTGRES_DB)

say()  { printf '\n\033[1m== %s\033[0m\n' "$*"; }
info() { printf '   %s\n' "$*"; }

sql() { docker compose exec -T postgres psql -U "$PG_USER" -d "$PG_DB" -tAc "$1"; }

# Starts the core stack if needed (workers are started by each script with its own settings).
ensure_stack() {
  docker compose up -d --no-build postgres redis rabbitmq migrator api testsite >/dev/null 2>&1
  wait_until "API healthy" 120 "curl -sf localhost:8080/health >/dev/null"
}

# Recreates the workers with the given environment overrides, e.g.: workers 2 QUEUE_MAX_ATTEMPTS=3
workers() {
  local count=$1; shift
  env "$@" docker compose up -d --no-build --force-recreate --scale worker="$count" worker >/dev/null 2>&1
  wait_until "$count workers consuming" 90 "[ \$(docker compose logs worker 2>&1 | grep -c 'Consuming crawl.tasks') -ge $count ]"
}

# Back to the defaults from .env / compose.
restore_workers() { docker compose up -d --no-build --force-recreate --scale worker="${1:-2}" worker >/dev/null 2>&1 || true; }

# wait_until "description" timeout_seconds "shell condition"
wait_until() {
  local description=$1 timeout=$2 condition=$3 start
  start=$(date +%s)
  until eval "$condition"; do
    if (( $(date +%s) - start > timeout )); then
      info "timed out after ${timeout}s waiting for: $description"
      return 1
    fi
    sleep 1
  done
}

# start_crawl <seed url inside testsite> → prints the job id
start_crawl() {
  curl -sf -X POST localhost:8080/crawls -H 'content-type: application/json' \
    -d "{\"siteId\":\"testsite\",\"seeds\":[\"$1\"]}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["jobId"])'
}

# Prints the worker container whose log shows it processing the task (waits for the log line: console logging is async).
worker_processing() {
  local task=$1 container
  for _ in $(seq 1 30); do
    for container in $(docker compose ps -q worker); do
      # grep -c reads everything: with pipefail, grep -q's early exit would make docker logs fail (SIGPIPE).
      if [ "$(docker logs "$container" 2>&1 | grep -c "Processing task $task")" -gt 0 ]; then echo "$container"; return 0; fi
    done
    sleep 0.5
  done
  return 1
}

job_status() { sql "select status from crawl_jobs where id = '$1'"; }

wait_for_job() { wait_until "job $1 completed" "${2:-180}" "[ \"\$(job_status $1)\" = Completed ]"; }

# check "label" "actual" "expected"
check() {
  if [[ "$2" == "$3" ]]; then
    printf '   \033[32mok\033[0m    %-58s %s\n' "$1" "$2"
  else
    printf '   \033[31mFAIL\033[0m  %-58s got %s, expected %s\n' "$1" "$2" "$3"
    FAILURES=$((FAILURES + 1))
  fi
}

# TestSite request times (seconds since the first) for paths containing the given text.
hit_times() {
  docker compose logs --no-log-prefix testsite | grep '^HIT ' | grep -F -- "$1" | python3 -c '
import sys
from datetime import datetime
times = [datetime.fromisoformat(line.split()[1]) for line in sys.stdin]
print(" ".join(f"{(t - times[0]).total_seconds():.1f}" for t in times))'
}

finish() {
  local elapsed=$(( $(date +%s) - STARTED_AT ))
  if (( FAILURES == 0 )); then
    printf '\n\033[32mRESULT: PASS\033[0m (%ss)\n' "$elapsed"
  else
    printf '\n\033[31mRESULT: FAIL\033[0m (%s check(s) failed, %ss)\n' "$FAILURES" "$elapsed"
    exit 1
  fi
}
