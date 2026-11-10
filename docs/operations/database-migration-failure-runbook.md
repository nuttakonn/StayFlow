# Database Migration Failure Runbook

## Symptoms
- `dotnet ef database update` exits with a non-zero code during deployment
- Application startup logs contain `PendingModelChangesWarning` or migration errors
- The application starts but throws `relation does not exist` or `column does not exist` errors

## Immediate actions

### 1. Do NOT start the new application version
If the migration failed, the schema is in an inconsistent state. Starting the new application against a partially-migrated schema causes unpredictable errors and potential data corruption.

### 2. Keep the previous version running (or start it if stopped)
```bash
# If the previous version was already stopped during deployment:
mv /opt/stayflow/releases/FAILED_VERSION /opt/stayflow/current_bad   # stash failed version
mv /opt/stayflow/releases/PREV_VERSION /opt/stayflow/current         # restore previous
sudo systemctl start stayflow-api
```
Verify: `curl -sf http://localhost:5000/healthz/ready | jq .`

### 3. Assess the migration state
```bash
# Connect to the database and check which migrations have been applied
psql -U stayflow_user stayflow_db -c \
  "SELECT \"MigrationId\", \"ProductVersion\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 10;"
```

Compare the last applied migration against what the failing deployment expected to run.

### 4. If the migration was partially applied

Partial application leaves the schema in an inconsistent state. Options:

**Option A — Restore from pre-deploy backup (safest):**
```bash
sudo systemctl stop stayflow-api
psql -U postgres -c "DROP DATABASE stayflow_db;"
psql -U postgres -c "CREATE DATABASE stayflow_db OWNER stayflow_user;"
psql -U stayflow_user stayflow_db < /backups/pre-deploy-YYYYMMDDHHMMSS.sql
sudo systemctl start stayflow-api
```
Data written after the backup is lost — see `rollback-runbook.md` for handling this with staff.

**Option B — Write a forward-fix migration:**
If restoring the backup would lose significant data, a developer must write a corrective migration that brings the schema to a clean state without data loss. This must be reviewed before applying.

### 5. Root cause investigation

After the system is stable:
- Review the failed migration file in `src/backend/StayFlow.Api/Migrations/`
- Run the failing SQL manually against a copy of the database to understand the exact error
- Common causes:
  - Unique constraint violation on existing data
  - Column rename without data migration
  - Adding a NOT NULL column to a table with existing rows without a default
  - A concurrent transaction holding a lock on the table

### 6. Prevention checklist for the next deployment
- [ ] Test migrations against a staging database that mirrors production data volume
- [ ] Prefer additive migrations (add columns/tables) over destructive ones
- [ ] For NOT NULL columns on existing tables, always add with a default or in a two-step migration
- [ ] Review the migration SQL output (`dotnet ef migrations script`) before deploying
