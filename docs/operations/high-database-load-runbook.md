# High Database Load Runbook

## Symptoms
- API responses are slow (> 2 seconds for simple requests)
- `/healthz/ready` shows the `database` check as `Degraded` or `Unhealthy`
- PostgreSQL logs show long-running queries or lock waits
- CPU or disk I/O on the database server is at 100%

## Immediate diagnosis

### 1. Identify long-running queries
```sql
SELECT pid, now() - pg_stat_activity.query_start AS duration,
       state, wait_event_type, wait_event, query
FROM pg_stat_activity
WHERE (now() - pg_stat_activity.query_start) > INTERVAL '5 seconds'
  AND state != 'idle'
ORDER BY duration DESC;
```

### 2. Identify locks
```sql
SELECT blocked.pid AS blocked_pid,
       blocking.pid AS blocking_pid,
       blocked.query AS blocked_query,
       blocking.query AS blocking_query
FROM pg_catalog.pg_locks blocked_locks
JOIN pg_catalog.pg_stat_activity blocked ON blocked.pid = blocked_locks.pid
JOIN pg_catalog.pg_locks blocking_locks
     ON blocking_locks.locktype = blocked_locks.locktype
     AND blocking_locks.pid != blocked_locks.pid
     AND blocking_locks.granted
JOIN pg_catalog.pg_stat_activity blocking ON blocking.pid = blocking_locks.pid;
```

### 3. Check table bloat / missing autovacuum
```sql
SELECT relname, n_dead_tup, n_live_tup,
       round(n_dead_tup::numeric / NULLIF(n_live_tup + n_dead_tup, 0) * 100, 1) AS dead_pct
FROM pg_stat_user_tables
ORDER BY n_dead_tup DESC LIMIT 10;
```

## Common causes and fixes

### Stuck or long-running transaction
If a transaction has been open for > 10 minutes and is blocking other queries:
```sql
-- Identify the PID
SELECT pid, query_start, state, query FROM pg_stat_activity WHERE state = 'idle in transaction';

-- Terminate it (safe — the transaction will be rolled back)
SELECT pg_terminate_backend(<pid>);
```

### Missing index on a frequently queried column
Check the slow query in `pg_stat_activity`. Compare against the indexed columns in `StayFlowDbContextModelSnapshot.cs`. If a query is doing a sequential scan on `reservations` or `booking_holds`, add an index.

The most critical indexes (already defined in migrations):
- `reservations`: `(organization_id)`, `(property_id, status)`, `(room_type_id, check_in_date, check_out_date)`
- `booking_holds`: `(property_id, room_type_id, status)`, `(expires_at_utc)`
- `guests`: `(organization_id)`

### Connection pool exhaustion
```sql
SELECT count(*), state FROM pg_stat_activity GROUP BY state;
```
If the number of active connections approaches `max_connections` (default 100), either:
- Increase `max_connections` in `postgresql.conf` and restart PostgreSQL (requires brief downtime)
- Or reduce the application's connection pool size in `appsettings.json`:
  ```json
  "ConnectionStrings": {
    "DefaultConnection": "...;Maximum Pool Size=20;"
  }
  ```

### High write load from reporting queries
Report endpoints run aggregate queries across reservations and folios. During peak check-in/check-out hours, avoid running large date-range reports. Schedule report exports for off-peak hours (after midnight).

## Escalation
If the database cannot be recovered within 15 minutes, assess whether to take the application offline temporarily to prevent partial write failures:
```bash
sudo systemctl stop stayflow-api
# Fix the database issue
sudo systemctl start stayflow-api
```

## Post-incident
- [ ] Record the slow query or lock chain that caused the problem
- [ ] Add a missing index if that was the cause
- [ ] Review whether a report or background job caused the spike
- [ ] Consider adding `pg_stat_statements` extension for ongoing slow query monitoring
