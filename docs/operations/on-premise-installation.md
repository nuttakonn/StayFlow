# StayFlow PMS — On-Premise Installation Guide

**Audience:** System administrator installing StayFlow PMS on a hotel's own server.  
**OS:** Ubuntu 22.04 LTS or 24.04 LTS (recommended). Other Debian-based distributions are supported but not tested.

---

## Pre-flight Checklist

Before starting, confirm:

- [ ] Server meets minimum hardware requirements (see §1)
- [ ] Ubuntu 22.04 LTS or 24.04 LTS is installed and updated
- [ ] You have `sudo` access on the server
- [ ] Ports 80 and 443 are open in the firewall / security group
- [ ] A domain name (e.g. `pms.yourhotel.com`) is pointed at the server's public IP
- [ ] You have the StayFlow release archive (`.tar.gz`) from your vendor
- [ ] PostgreSQL password and app secret are ready (generate random strings — do NOT use examples from this document)

---

## 1. System Requirements

| Resource | Minimum (small hotel, ≤50 rooms) | Recommended |
|---|---|---|
| CPU | 2 vCPU | 4 vCPU |
| RAM | 4 GB | 8 GB |
| Disk | 50 GB SSD | 100 GB SSD |
| OS | Ubuntu 22.04 LTS | Ubuntu 24.04 LTS |
| Network | Public IP + DNS record | Dedicated IP |

**Required open ports:**

| Port | Protocol | Purpose |
|---|---|---|
| 80 | TCP | HTTP (redirects to HTTPS) |
| 443 | TCP | HTTPS |

Port 5000 (the backend API) must **not** be exposed directly to the internet. Only the reverse proxy (Caddy or nginx) faces the internet.

---

## 2. Update the System

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y curl wget gnupg2 software-properties-common unzip
```

---

## 3. Install .NET 10 Runtime

The server needs only the runtime — not the full SDK.

```bash
# Add Microsoft package feed
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt update
sudo apt install -y aspnetcore-runtime-10.0
```

Verify:

```bash
dotnet --list-runtimes
# Should show: Microsoft.AspNetCore.App 10.x.x
```

---

## 4. Install PostgreSQL 17

```bash
# Add PostgreSQL official repository
sudo sh -c 'echo "deb https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
wget --quiet -O - https://www.postgresql.org/media/keys/ACCC4CF8.asc | sudo apt-key add -

sudo apt update
sudo apt install -y postgresql-17 postgresql-client-17
```

Verify:

```bash
sudo systemctl status postgresql
# Should show: active (running)
```

---

## 5. Create System User and Directories

```bash
# Create a dedicated non-root user for the application
sudo useradd -r -s /bin/false -m -d /opt/stayflow stayflow

# Create directory structure
sudo mkdir -p /opt/stayflow/{api,web,data,logs,backups}
sudo chown -R stayflow:stayflow /opt/stayflow
```

---

## 6. Configure PostgreSQL

```bash
sudo -u postgres psql <<'EOF'
-- Replace 'REPLACE_WITH_STRONG_PASSWORD' with your actual password
CREATE USER stayflow_user WITH PASSWORD 'REPLACE_WITH_STRONG_PASSWORD';
CREATE DATABASE stayflow_db OWNER stayflow_user;
GRANT ALL PRIVILEGES ON DATABASE stayflow_db TO stayflow_user;
\c stayflow_db
GRANT ALL ON SCHEMA public TO stayflow_user;
EOF
```

> **Important:** Use a strong random password (at least 20 characters). Store it securely — you will need it in §8.

---

## 7. Deploy the Application Files

Extract the release archive provided by your vendor:

```bash
# Replace v1.0.0 with the actual version
tar -xzf stayflow-v1.0.0.tar.gz -C /tmp/stayflow-release

# Deploy backend API
sudo cp -r /tmp/stayflow-release/api/* /opt/stayflow/api/
sudo chown -R stayflow:stayflow /opt/stayflow/api/

# Deploy frontend web files
sudo cp -r /tmp/stayflow-release/web/* /opt/stayflow/web/
sudo chown -R stayflow:stayflow /opt/stayflow/web/

# Clean up
rm -rf /tmp/stayflow-release
```

> **Note:** If you are building from source (developers only), see the developer README for `dotnet publish` and `npm run build` commands.

---

## 8. Create the Production Configuration File

```bash
sudo nano /opt/stayflow/api/appsettings.Production.json
```

Paste the following template and **fill in every value marked with `REPLACE_`**:

```json
{
  "Deployment": {
    "Mode": "OnPremise"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=stayflow_db;Username=stayflow_user;Password=REPLACE_WITH_DB_PASSWORD;Include Error Detail=false"
  },
  "Jwt": {
    "Secret": "REPLACE_WITH_RANDOM_64_CHAR_SECRET",
    "Issuer": "stayflow-pms",
    "Audience": "stayflow-pms",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 30
  },
  "Cors": {
    "AllowedOrigins": ["https://pms.yourhotel.com"]
  },
  "RateLimiting": {
    "AuthPermitLimit": 10,
    "AuthWindowSeconds": 60,
    "ApiPermitLimit": 100,
    "ApiWindowSeconds": 60
  },
  "ReverseProxy": {
    "KnownProxies": ["127.0.0.1"],
    "KnownNetworks": []
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

**Generate a random 64-character JWT secret:**

```bash
openssl rand -base64 48
```

Secure the file so only the `stayflow` user can read it:

```bash
sudo chown stayflow:stayflow /opt/stayflow/api/appsettings.Production.json
sudo chmod 600 /opt/stayflow/api/appsettings.Production.json
```

---

## 9. Run Database Migrations

The first run creates all database tables.

```bash
sudo -u stayflow bash -c '
  cd /opt/stayflow/api
  ASPNETCORE_ENVIRONMENT=Production \
  dotnet StayFlow.Api.dll --migrate-only
'
```

If the application does not support `--migrate-only`, you can run migrations via the EF tools on a machine with the SDK installed:

```bash
# On a machine with .NET SDK 10 installed (e.g. developer's laptop)
# with access to the production database
dotnet ef database update \
  --project src/backend/StayFlow.Api \
  --connection "Host=YOUR_SERVER_IP;Port=5432;Database=stayflow_db;Username=stayflow_user;Password=YOUR_PASSWORD"
```

Verify migrations succeeded:

```bash
sudo -u postgres psql -d stayflow_db -c "\dt" | head -40
# Should list 30+ tables: Organizations, Properties, Rooms, Reservations, etc.
```

---

## 10. Create the systemd Service

```bash
sudo nano /etc/systemd/system/stayflow-api.service
```

Paste:

```ini
[Unit]
Description=StayFlow PMS API
After=network.target postgresql.service
Requires=postgresql.service

[Service]
Type=exec
User=stayflow
Group=stayflow
WorkingDirectory=/opt/stayflow/api
ExecStart=/usr/bin/dotnet StayFlow.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGTERM
TimeoutStopSec=30

Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Harden the service
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/opt/stayflow/data /opt/stayflow/logs
PrivateTmp=true

StandardOutput=append:/opt/stayflow/logs/api.log
StandardError=append:/opt/stayflow/logs/api-error.log

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable stayflow-api
sudo systemctl start stayflow-api
```

---

## 11. Install and Configure Caddy (Reverse Proxy + TLS)

Caddy automatically obtains and renews Let's Encrypt TLS certificates.

```bash
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update
sudo apt install caddy
```

Create the Caddyfile:

```bash
sudo nano /etc/caddy/Caddyfile
```

Paste (replace `pms.yourhotel.com` with your actual domain):

```
pms.yourhotel.com {
    # Serve the React SPA for all non-API paths
    handle /api/* {
        reverse_proxy localhost:5000
    }

    handle /healthz {
        reverse_proxy localhost:5000
    }

    handle {
        root * /opt/stayflow/web
        try_files {path} /index.html
        file_server
    }

    # Security headers
    header {
        X-Content-Type-Options nosniff
        X-Frame-Options DENY
        Referrer-Policy strict-origin-when-cross-origin
        Permissions-Policy "camera=(), microphone=(), geolocation=()"
        -Server
    }

    encode gzip
    log {
        output file /opt/stayflow/logs/caddy-access.log
    }
}
```

Reload Caddy:

```bash
sudo systemctl reload caddy
sudo systemctl status caddy
```

> **TLS note:** Caddy will automatically request a certificate from Let's Encrypt the first time a request arrives. The domain must resolve to this server's public IP before starting Caddy. See `docs/operations/tls-configuration.md` for advanced TLS options including nginx and self-signed certificates.

---

## 12. First-Run Verification

**1. Check the API health:**

```bash
curl -s http://localhost:5000/healthz
# Expected: {"status":"Healthy"} or similar JSON
```

**2. Check the service status:**

```bash
sudo systemctl status stayflow-api
# Should show: active (running)
```

**3. Check the application logs:**

```bash
sudo tail -50 /opt/stayflow/logs/api.log
# Should show startup messages, no errors
```

**4. Test HTTPS from outside:**

```bash
curl -s https://pms.yourhotel.com/healthz
# Should return healthy response (run from a different machine)
```

**5. Open the browser:**

Navigate to `https://pms.yourhotel.com`. The StayFlow login page should appear.

**6. Create the first admin account:**

On first launch, the system may prompt to create the initial organization and admin user. Follow the on-screen setup wizard.

---

## 13. Upgrade Procedure

**Always back up the database before upgrading** (see `docs/operations/backup-runbook.md`).

```bash
# 1. Download and extract the new release
tar -xzf stayflow-v1.1.0.tar.gz -C /tmp/stayflow-new

# 2. Keep the previous version as a fallback
sudo mv /opt/stayflow/api /opt/stayflow/api.previous
sudo mv /opt/stayflow/web /opt/stayflow/web.previous

# 3. Deploy new files
sudo mkdir -p /opt/stayflow/{api,web}
sudo cp -r /tmp/stayflow-new/api/* /opt/stayflow/api/
sudo cp -r /tmp/stayflow-new/web/* /opt/stayflow/web/
sudo chown -R stayflow:stayflow /opt/stayflow/api /opt/stayflow/web

# 4. Restore the production config (never overwrite it from the archive)
sudo cp /opt/stayflow/api.previous/appsettings.Production.json /opt/stayflow/api/

# 5. Stop the service
sudo systemctl stop stayflow-api

# 6. Run migrations (new release may add tables/columns)
sudo -u stayflow bash -c '
  cd /opt/stayflow/api
  ASPNETCORE_ENVIRONMENT=Production dotnet StayFlow.Api.dll --migrate-only
'

# 7. Start the service
sudo systemctl start stayflow-api

# 8. Verify health
sleep 5
curl -s http://localhost:5000/healthz

# 9. Clean up temporary files
rm -rf /tmp/stayflow-new
```

---

## 14. Rollback Procedure

If the upgrade fails and the service does not start correctly:

```bash
# 1. Stop the broken service
sudo systemctl stop stayflow-api

# 2. Swap back to the previous version
sudo mv /opt/stayflow/api /opt/stayflow/api.failed
sudo mv /opt/stayflow/api.previous /opt/stayflow/api
sudo mv /opt/stayflow/web /opt/stayflow/web.failed
sudo mv /opt/stayflow/web.previous /opt/stayflow/web

# 3. Restore the config (should already be there, but confirm)
ls -la /opt/stayflow/api/appsettings.Production.json

# 4. Start the previous version
sudo systemctl start stayflow-api

# 5. Verify
curl -s http://localhost:5000/healthz
```

> **Database note:** If the new version ran migrations that modified the schema, rolling back the application may not be sufficient. In that case, restore from a database backup taken before the upgrade. See `docs/operations/backup-runbook.md`.

---

## 15. Log Rotation

Prevent log files from filling the disk:

```bash
sudo nano /etc/logrotate.d/stayflow
```

Paste:

```
/opt/stayflow/logs/*.log {
    daily
    rotate 14
    compress
    delaycompress
    missingok
    notifempty
    create 0640 stayflow stayflow
    postrotate
        systemctl kill --signal=SIGUSR1 stayflow-api 2>/dev/null || true
    endscript
}
```

---

## 16. Firewall Configuration (UFW)

```bash
sudo ufw allow 22/tcp    # SSH — only allow from your IP in production
sudo ufw allow 80/tcp    # HTTP (Caddy redirects to HTTPS)
sudo ufw allow 443/tcp   # HTTPS
sudo ufw enable
sudo ufw status
```

Do **not** open port 5000 (the backend API). It must only be accessible from localhost via the reverse proxy.

---

## 17. Troubleshooting

| Symptom | Check |
|---|---|
| Service fails to start | `sudo journalctl -u stayflow-api -n 50` |
| Database connection error | Verify `appsettings.Production.json` password, run `sudo -u stayflow psql "Host=localhost;Database=stayflow_db;Username=stayflow_user;Password=..."` |
| 502 Bad Gateway from Caddy | API not running: `curl http://localhost:5000/healthz` |
| Login page shows but API calls fail | Check CORS `AllowedOrigins` in config — must match your domain exactly with `https://` |
| TLS certificate not issued | Domain DNS not pointing to this server; check `sudo journalctl -u caddy` |
| "Migrations pending" error | Run migration step from §9 |
| Logs filling disk | Confirm logrotate is configured (§15); check `df -h` |

---

## Related Documents

- `docs/operations/tls-configuration.md` — nginx alternative, self-signed certs, certificate renewal
- `docs/operations/backup-runbook.md` — automated backups, encryption, restore drill, RPO/RTO
- `docs/adr/` — architecture decisions (deployment mode, multi-tenancy, authentication)
- `.env.example` — full list of environment variables with descriptions
