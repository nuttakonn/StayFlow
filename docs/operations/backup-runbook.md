# Backup and Recovery Runbook

This runbook defines backup procedures, retention policies, restore
steps, and recovery targets for StayFlow PMS on-premise deployments.

**A backup is not verified until a restore has been tested.**

---

## Recovery targets

Define these values with the hotel before go-live and record them here:

| Metric | Definition | Default target |
|---|---|---|
| **RPO** (Recovery Point Objective) | Maximum data loss if the server fails | 24 hours |
| **RTO** (Recovery Time Objective) | Maximum time to restore service | 4 hours |

If the hotel requires tighter targets, increase backup frequency (see
below) and test the restore procedure against the RTO.

---

## What must be backed up

| Component | Location | Method |
|---|---|---|
| PostgreSQL database | Managed by PostgreSQL | `pg_dump` (logical) or `pg_basebackup` (physical) |
| Uploaded branding assets | `/opt/stayflow/uploads/` | File copy / rsync |
| Application configuration | `/etc/stayflow/` | File copy |
| TLS certificates (if self-managed) | `/etc/ssl/stayflow/` | File copy |

The application binary itself does not need to be backed up — restore
it from the release package.

---

## Backup schedule

| Frequency | Type | Retention |
|---|---|---|
| Daily at 02:00 | Full PostgreSQL dump | 7 days |
| Weekly (Sunday 03:00) | Full dump + files | 4 weeks |
| Monthly (1st at 04:00) | Full dump + files | 12 months |

The daily job takes under 5 minutes for a typical small-hotel database.
Adjust as needed.

---

## Backup script

Place at `/opt/stayflow/scripts/backup.sh` and make executable
(`chmod +x`).

```bash
#!/usr/bin/env bash
set -euo pipefail

# --- Configuration ---
BACKUP_DIR="/var/backups/stayflow"
DB_NAME="stayflow_db"
DB_USER="stayflow_app"
DB_HOST="localhost"
DB_PORT="5432"
UPLOAD_DIR="/opt/stayflow/uploads"
RETENTION_DAILY=7     # days
RETENTION_WEEKLY=28   # days
LOG_FILE="/var/log/stayflow/backup.log"

# Encryption passphrase — store in /etc/stayflow/backup.env, never hardcode here
# File: /etc/stayflow/backup.env
# BACKUP_PASSPHRASE=change_this_to_a_long_random_string
source /etc/stayflow/backup.env

# --- Setup ---
mkdir -p "$BACKUP_DIR/daily" "$BACKUP_DIR/weekly" "$BACKUP_DIR/monthly"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BASENAME="stayflow_${TIMESTAMP}"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }

# --- Database dump ---
log "Starting database backup: $DB_NAME"
DUMP_FILE="$BACKUP_DIR/daily/${BASENAME}.dump"
PGPASSWORD="" pg_dump \
  -h "$DB_HOST" -p "$DB_PORT" \
  -U "$DB_USER" \
  -F c \
  --no-password \
  "$DB_NAME" \
  > "$DUMP_FILE"
log "Database dump completed: $DUMP_FILE ($(du -sh "$DUMP_FILE" | cut -f1))"

# --- Encrypt the dump ---
gpg --batch --yes --symmetric \
  --passphrase "$BACKUP_PASSPHRASE" \
  --cipher-algo AES256 \
  --output "${DUMP_FILE}.gpg" \
  "$DUMP_FILE"
rm "$DUMP_FILE"
log "Encrypted: ${DUMP_FILE}.gpg"

# --- Backup uploads (weekly and monthly jobs add this) ---
if [[ "${1:-daily}" != "daily" ]]; then
  TAR_FILE="$BACKUP_DIR/daily/${BASENAME}_uploads.tar.gz.gpg"
  tar -czf - "$UPLOAD_DIR" 2>/dev/null \
    | gpg --batch --yes --symmetric \
        --passphrase "$BACKUP_PASSPHRASE" \
        --cipher-algo AES256 \
        --output "$TAR_FILE"
  log "Uploads backup: $TAR_FILE"
fi

# --- Prune old daily backups ---
find "$BACKUP_DIR/daily" -name "*.gpg" -mtime "+${RETENTION_DAILY}" -delete
log "Pruned daily backups older than ${RETENTION_DAILY} days"

# --- Weekly copy ---
DOW=$(date +%u)   # 1=Mon … 7=Sun
if [[ "$DOW" == "7" ]]; then
  cp "${DUMP_FILE}.gpg" "$BACKUP_DIR/weekly/"
  find "$BACKUP_DIR/weekly" -name "*.gpg" -mtime "+${RETENTION_WEEKLY}" -delete
  log "Weekly backup retained"
fi

# --- Monthly copy ---
DOM=$(date +%d)
if [[ "$DOM" == "01" ]]; then
  MONTH_DIR="$BACKUP_DIR/monthly/$(date +%Y%m)"
  mkdir -p "$MONTH_DIR"
  cp "${DUMP_FILE}.gpg" "$MONTH_DIR/"
  log "Monthly backup retained: $MONTH_DIR"
fi

log "Backup complete."
```

### Set up the backup passphrase file

```bash
sudo bash -c 'cat > /etc/stayflow/backup.env << EOF
BACKUP_PASSPHRASE=replace_with_a_long_random_passphrase_do_not_use_this_default
EOF'
sudo chmod 600 /etc/stayflow/backup.env
```

**Keep a copy of the passphrase in a secure location separate from the
server.** Without it, encrypted backups cannot be restored.

### Schedule with cron

```bash
sudo crontab -e
```

Add:

```cron
# StayFlow daily backup at 02:00
0 2 * * * /opt/stayflow/scripts/backup.sh daily >> /var/log/stayflow/backup.log 2>&1

# StayFlow weekly backup at 03:00 on Sunday (also captures uploads)
0 3 * * 0 /opt/stayflow/scripts/backup.sh weekly >> /var/log/stayflow/backup.log 2>&1
```

### Verify cron will run

```bash
sudo crontab -l
# Test manual run:
sudo /opt/stayflow/scripts/backup.sh daily
ls -lh /var/backups/stayflow/daily/
```

---

## Off-site / remote backup

Keeping backups only on the server means a disk failure or theft loses
both the application and the backups. Copy daily dumps to an off-site
location.

### Option A — rclone to cloud storage (Backblaze B2, S3, Google Drive)

```bash
sudo apt install -y rclone
# Configure: sudo rclone config
# Then add to cron after the backup job:
rclone copy /var/backups/stayflow/daily/ remote:stayflow-backups/daily/ \
  --max-age 8d --log-file /var/log/stayflow/rclone.log
```

### Option B — rsync to a secondary server on-site

```bash
rsync -az --delete \
  /var/backups/stayflow/ \
  backup-user@192.168.1.200:/backups/stayflow/
```

---

## Restore procedure

**Perform a restore drill at least once every 90 days to verify backups
are usable.**

### Step 1 — Stop the application

```bash
sudo systemctl stop stayflow-api
```

### Step 2 — Decrypt the backup

```bash
source /etc/stayflow/backup.env
gpg --batch --yes --decrypt \
  --passphrase "$BACKUP_PASSPHRASE" \
  --output /tmp/stayflow_restore.dump \
  /var/backups/stayflow/daily/stayflow_YYYYMMDD_HHMMSS.dump.gpg
```

Replace `stayflow_YYYYMMDD_HHMMSS` with the actual file name.

### Step 3 — Create a fresh database (if restoring to a clean server)

```bash
sudo -u postgres psql <<SQL
CREATE ROLE stayflow_app WITH LOGIN PASSWORD 'replace_with_your_password';
CREATE DATABASE stayflow_db OWNER stayflow_app;
SQL
```

Skip this step if the database already exists and you are overwriting it.

### Step 4 — Restore the dump

```bash
# Drop existing schema (destructive — only on a confirmed bad database)
sudo -u postgres dropdb --if-exists stayflow_db
sudo -u postgres createdb --owner stayflow_app stayflow_db

# Restore
pg_restore \
  -h localhost -U stayflow_app \
  -d stayflow_db \
  --no-owner --role stayflow_app \
  /tmp/stayflow_restore.dump
```

### Step 5 — Run any pending migrations

If restoring an older backup to a newer application version, run
migrations to bring the schema up to date:

```bash
cd /opt/stayflow/api
dotnet StayFlow.Api.dll --migrate-only
# Or via dotnet ef (if EF tools are installed):
# dotnet ef database update --project /opt/stayflow/src/StayFlow.Api
```

### Step 6 — Restore uploaded files (if applicable)

```bash
source /etc/stayflow/backup.env
gpg --batch --yes --decrypt \
  --passphrase "$BACKUP_PASSPHRASE" \
  --output - \
  /var/backups/stayflow/daily/stayflow_YYYYMMDD_HHMMSS_uploads.tar.gz.gpg \
  | tar -xzf - -C /
```

### Step 7 — Start and verify

```bash
sudo systemctl start stayflow-api
sleep 5
curl -s http://localhost:5000/healthz
```

Expected response: `{"status":"Healthy",...}`

### Step 8 — Clean up temporary files

```bash
rm -f /tmp/stayflow_restore.dump
```

---

## Restore drill checklist

Run this checklist every 90 days on a test server (never on the live
database in production without a backup).

- [ ] Most recent backup file exists and is not empty
- [ ] Passphrase decrypts the file successfully
- [ ] `pg_restore` completes without errors
- [ ] Application starts and health check passes
- [ ] Log in and verify data is present (check a reservation from last week)
- [ ] Upload a test logo and verify the file is restored
- [ ] Record the drill date and restore duration in this document

**Last drill date:** _______________  
**Restore duration:** _______________  
**Performed by:** _______________

---

## Monitoring and alerting

The backup script exits non-zero on failure (due to `set -euo pipefail`).
Add a monitoring check that alerts if no backup file has been created in
the last 25 hours.

Simple file-age check (add to a monitoring cron):

```bash
#!/usr/bin/env bash
LATEST=$(find /var/backups/stayflow/daily -name "*.gpg" -mmin -1500 | head -1)
if [[ -z "$LATEST" ]]; then
  echo "ALERT: No StayFlow backup in the last 25 hours" | mail -s "Backup FAILED" admin@yourhotel.com
fi
```

---

## Log locations

| Log | Path |
|---|---|
| Backup script | `/var/log/stayflow/backup.log` |
| rclone sync | `/var/log/stayflow/rclone.log` |
| Application | `journalctl -u stayflow-api` |

Rotate logs weekly and keep 4 weeks:

```bash
# /etc/logrotate.d/stayflow
/var/log/stayflow/*.log {
    weekly
    rotate 4
    compress
    missingok
    notifempty
    create 0640 root adm
}
```
