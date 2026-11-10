# Background Job Failure Runbook

## Background jobs in StayFlow PMS

| Job | Class | Schedule | Purpose |
|-----|-------|----------|---------|
| Daily report email | `DailyReportBackgroundService` | Configurable UTC hour | Sends daily occupancy digest to configured recipients |
| Booking hold cleanup | `BookingHoldCleanupService` | Every 5 minutes | Expires stale booking engine holds |

## Detecting failures

Application logs include job start/completion events. Look for:
```bash
journalctl -u stayflow-api --since "today" | grep -i "BackgroundService\|job\|DailyReport\|BookingHold"
```

Missed jobs are visible as: a property whose daily report was not received, or booking holds that did not expire.

## Daily report email job

### Symptom
Property manager reports they did not receive the expected daily email digest.

### Investigation
```bash
# Check logs for the expected send time
journalctl -u stayflow-api --since "yesterday" | grep -i "DailyReport"
```

Look for:
- `SmtpException` — SMTP credentials wrong, provider down, or quota exceeded
- `No recipients configured` — `EmailNotificationSettings.DailyReportRecipients` is empty for the property
- Job not running — the application may have been stopped or restarted around the scheduled hour

### Manual trigger (for testing or catch-up)
```
POST /api/v1/admin/daily-report/trigger
Authorization: Bearer <admin-token>
X-Organization-Id: <org-id>
```
This runs the report immediately regardless of schedule.

### Resolution
- Fix SMTP credentials in the environment configuration and restart
- Or add recipients: `PUT /api/v1/properties/{propId}/email-settings`

## Booking hold cleanup job

### Symptom
Guests report they cannot book a room that appears available, or holds are not being released after their timer expires.

### Investigation
```bash
# Check for holds that should have expired
psql -U stayflow_user stayflow_db -c \
  "SELECT id, property_id, expires_at_utc, status FROM booking_holds
   WHERE status = 0 AND expires_at_utc < NOW() ORDER BY expires_at_utc DESC LIMIT 20;"
```
A large number of `status = 0` (Active) holds with `expires_at_utc` in the past means the cleanup job is not running.

```bash
# Check whether the job is logging
journalctl -u stayflow-api --since "1 hour ago" | grep -i "BookingHoldCleanup"
```

### Resolution
If the application is running but the job is not logging:
```bash
sudo systemctl restart stayflow-api
```
The job runs every 5 minutes after startup. If it was silently failing due to a database error, the restart will surface the error in logs.

If the application cannot be restarted immediately, manually expire stale holds:
```sql
UPDATE booking_holds
SET status = 2  -- Expired
WHERE status = 0 AND expires_at_utc < NOW();
```

## General job failure principles
- Background job failures never crash the application — they are logged and retried on the next cycle
- A single failed cycle is not an incident; multiple consecutive failures are
- After any fix, verify the job runs successfully on its next cycle before closing the incident
