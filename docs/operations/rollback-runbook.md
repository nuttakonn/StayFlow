# Rollback Runbook

## When to use this
Trigger a rollback when, within 30 minutes of a deployment:
- `/healthz/ready` returns `Unhealthy`
- Repeated 500 errors from the API
- A critical business workflow (check-in, payment, reservation) is broken
- Database migration caused data corruption

## Rollback decision: application vs. database

| Situation | Action |
|-----------|--------|
| Application bug (no schema change) | Roll back application binary only |
| Migration added a column / index | Roll back application binary only (column stays — it's safe) |
| Migration dropped a column / table | **Stop. Contact the team.** Column drops are destructive; rolling back the app binary alone will not restore data. Use the pre-deploy backup. |
| Data corruption observed | Restore from pre-deploy backup (see `backup-runbook.md`) |

## Application-only rollback (most cases)

### 1. Stop the current service
```bash
sudo systemctl stop stayflow-api
```

### 2. Swap back to the previous release
```bash
mv /opt/stayflow/current /opt/stayflow/releases/FAILED_VERSION
mv /opt/stayflow/releases/PREV_VERSION /opt/stayflow/current
```

### 3. Start the previous version
```bash
sudo systemctl start stayflow-api
sleep 10
curl -sf http://localhost:5000/healthz/ready | jq .
```

### 4. Verify the system is healthy
- Log in and confirm the dashboard loads
- Verify `/healthz/ready` shows `Healthy`

### 5. Notify staff

## Database restore rollback (destructive migration case)

This is the last resort. It will lose all data written since the pre-deploy backup.

```bash
sudo systemctl stop stayflow-api

# Drop the damaged database and restore from backup
psql -U postgres -c "DROP DATABASE stayflow_db;"
psql -U postgres -c "CREATE DATABASE stayflow_db OWNER stayflow_user;"
psql -U stayflow_user stayflow_db < /backups/pre-deploy-YYYYMMDDHHMMSS.sql

# Roll back to the previous application binary
mv /opt/stayflow/current /opt/stayflow/releases/FAILED_VERSION
mv /opt/stayflow/releases/PREV_VERSION /opt/stayflow/current

sudo systemctl start stayflow-api
```

**After a database restore:** notify all staff that any transactions entered after the deployment backup are lost and must be re-entered manually. Capture the list of affected reservations, payments, and check-ins from staff before they re-enter data.

## Post-rollback
- [ ] Record the rollback: date, version rolled back from, version restored to, reason
- [ ] File a post-mortem: what broke, why, what prevents it next time
- [ ] Do not re-deploy the failed version until the root cause is fixed and the fix is tested
