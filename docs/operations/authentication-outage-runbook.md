# Authentication Outage Runbook

## Symptoms
- `/api/v1/auth/login` returns 500 or is unreachable
- All users are suddenly logged out and cannot log back in
- `/healthz/ready` shows the database as `Unhealthy`
- JWT validation errors appear for all requests (`401 Unauthorized`)

## Diagnosis by symptom

### Database is down (most common cause)
```bash
# Check DB connectivity
psql -U stayflow_user stayflow_db -c "SELECT 1;" 2>&1
# Check DB service
sudo systemctl status postgresql
```
If PostgreSQL is down, restart it:
```bash
sudo systemctl restart postgresql
# Then verify the app recovers on its own
curl -sf http://localhost:5000/healthz/ready | jq .
```

### JWT secret changed or missing
If the `Jwt__Secret` environment variable is missing or has changed, all issued tokens become invalid.

```bash
# Check if the secret is set
echo "Jwt__Secret length: ${#JWT__SECRET}"
# If empty, the application falls back to an insecure default and all tokens are invalid
```

Fix: restore the original secret value from the secrets manager / `.env` backup and restart the service.
```bash
sudo systemctl restart stayflow-api
```

**Important:** Do not change the JWT secret while users are logged in. Any change invalidates all active sessions. Users will need to log in again.

### Clock skew between server and clients
JWT validation rejects tokens whose `nbf` or `exp` is outside the allowed `ClockSkew` (currently 1 minute). If the server clock drifts significantly, all tokens fail.

```bash
# Check server time
date -u
# Sync the clock
sudo timedatectl set-ntp true
sudo chronyc makestep   # or: sudo ntpdate -u pool.ntp.org
```

### Application crash loop
```bash
journalctl -u stayflow-api -n 100 --no-pager | grep -E "fail|error|exception" -i
```

If the app is crash-looping, identify the root cause in logs before restarting.

## Recovery verification
After any fix:
```bash
# Health check
curl -sf http://localhost:5000/healthz/ready | jq .

# Login test
curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"demo@stayflow.test","password":"StayFlowDemo2026!"}' | jq .accessToken
```

## Notify staff
If users cannot log in for more than 5 minutes during working hours, notify front-desk staff so they know the system is temporarily unavailable and to hold off on check-ins until it recovers.

## Post-incident
- [ ] Record root cause and fix
- [ ] Check whether any transactions were lost during the outage window
- [ ] Review monitoring — alerts should have fired before staff noticed
