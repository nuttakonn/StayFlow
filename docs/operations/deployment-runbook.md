# Deployment Runbook

## Overview
Steps to deploy a new version of StayFlow PMS to the on-premise server. This is a low-downtime deployment using a staged swap.

## Prerequisites
- SSH access to the production server
- The new application build artifact (`.tar.gz` or Docker image)
- Database migration scripts reviewed and approved
- Rollback plan confirmed (see `rollback-runbook.md`)

## Pre-deployment checklist
- [ ] New version has passed all integration tests in CI
- [ ] Database migrations reviewed — confirm they are forward-only safe
- [ ] Notify staff of a brief maintenance window (5–10 minutes)
- [ ] Check current disk space: `df -h`
- [ ] Verify last backup completed successfully: check backup log or run a manual backup

## Deployment steps

### 1. Take a database backup immediately before deploying
```bash
pg_dump -U stayflow_user stayflow_db > /backups/pre-deploy-$(date +%Y%m%d%H%M%S).sql
```

### 2. Download and verify the new artifact
```bash
# Download to a staging directory, never deploy directly from /tmp
mkdir -p /opt/stayflow/releases/NEW_VERSION
cd /opt/stayflow/releases/NEW_VERSION
# Verify checksum provided by CI
sha256sum -c stayflow-api.tar.gz.sha256
```

### 3. Stop the current service
```bash
sudo systemctl stop stayflow-api
```

### 4. Apply database migrations
```bash
cd /opt/stayflow/releases/NEW_VERSION
dotnet ef database update --connection "$CONNECTION_STRING"
# Confirm: "Done. No pending migrations."
```

If migrations fail, do NOT start the new version. Follow `database-migration-failure-runbook.md`.

### 5. Swap the release
```bash
# Keep the previous release for rollback
mv /opt/stayflow/current /opt/stayflow/releases/PREV_VERSION
mv /opt/stayflow/releases/NEW_VERSION /opt/stayflow/current
```

### 6. Start the service
```bash
sudo systemctl start stayflow-api
# Wait 10 seconds, then verify
sleep 10
curl -sf http://localhost:5000/healthz/ready | jq .
```

A healthy response looks like:
```json
{ "status": "Healthy", "checks": [ { "name": "database", "status": "Healthy" } ] }
```

### 7. Smoke test
- Log in to the front desk UI
- Verify the dashboard loads
- Check `/healthz/live` and `/healthz/ready` both return `Healthy`

### 8. Notify staff that the system is back online

## Rollback trigger
If `/healthz/ready` shows `Unhealthy`, or the application returns 500 errors, follow `rollback-runbook.md` immediately. Do not wait more than 5 minutes to decide.

## Post-deployment
- [ ] Record the deployment in the change log (date, version, operator)
- [ ] Monitor error logs for 30 minutes: `journalctl -u stayflow-api -f`
- [ ] Confirm the previous backup artifact is retained for at least 7 days
