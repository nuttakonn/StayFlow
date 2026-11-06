# TLS Configuration Guide

StayFlow PMS must run behind HTTPS in every non-local environment.
This guide covers two supported approaches: **Caddy** (recommended for
simplicity) and **nginx + Let's Encrypt** (for environments that
already use nginx).

Kestrel (the .NET runtime) listens on plain HTTP port 5000. The reverse
proxy handles TLS termination and forwards to Kestrel over HTTP on the
loopback interface. Never expose port 5000 directly to the internet.

---

## Pre-flight checklist

- [ ] A domain name that resolves to this server's public IP
- [ ] Ports 80 and 443 open in the firewall / security group
- [ ] StayFlow API running and healthy on `http://localhost:5000/healthz`
- [ ] Frontend static files built and placed (see on-premise installation guide)

---

## Option A — Caddy (recommended)

Caddy handles certificate issuance, renewal, and HTTP-to-HTTPS redirect
automatically. No `certbot` cron job needed.

### Install Caddy

```bash
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
  | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
  | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install -y caddy
```

### Configure Caddy

Replace `pms.yourhotel.com` with your actual domain.

```
# /etc/caddy/Caddyfile

pms.yourhotel.com {
    # Serve React SPA static files
    root * /opt/stayflow/frontend
    try_files {path} /index.html

    # Forward API requests to Kestrel
    handle /api/* {
        reverse_proxy localhost:5000
    }

    handle /healthz {
        reverse_proxy localhost:5000
    }

    # Serve SPA for all other paths
    handle {
        file_server
    }

    # Security headers
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
        X-Frame-Options DENY
        X-Content-Type-Options nosniff
        Referrer-Policy strict-origin-when-cross-origin
        -Server
    }

    encode gzip zstd

    log {
        output file /var/log/stayflow/caddy-access.log {
            roll_size 50mb
            roll_keep 7
        }
    }
}
```

### Configure per-IP rate limiting

Tell Kestrel that Caddy is a trusted proxy so the real client IP is used:

```bash
# /etc/stayflow/environment
ReverseProxy__KnownProxies__0=127.0.0.1
```

Reload the StayFlow service after updating the env file:

```bash
sudo systemctl restart stayflow-api
```

### Start Caddy

```bash
sudo systemctl enable --now caddy
# Validate config
sudo caddy validate --config /etc/caddy/Caddyfile
# Reload without downtime
sudo systemctl reload caddy
```

Caddy issues a Let's Encrypt certificate on first request. Logs:

```bash
sudo journalctl -u caddy -f
```

---

## Option B — nginx + Let's Encrypt

Use this if nginx is already deployed or required by your infrastructure
team.

### Install nginx and certbot

```bash
sudo apt update
sudo apt install -y nginx certbot python3-certbot-nginx
```

### Obtain a certificate

```bash
sudo certbot certonly --nginx -d pms.yourhotel.com
```

Certificates are stored at:
- `/etc/letsencrypt/live/pms.yourhotel.com/fullchain.pem`
- `/etc/letsencrypt/live/pms.yourhotel.com/privkey.pem`

Certbot installs a cron entry for automatic renewal. Verify it:

```bash
sudo certbot renew --dry-run
```

### Configure nginx

```nginx
# /etc/nginx/sites-available/stayflow

# Redirect HTTP → HTTPS
server {
    listen 80;
    server_name pms.yourhotel.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name pms.yourhotel.com;

    ssl_certificate     /etc/letsencrypt/live/pms.yourhotel.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/pms.yourhotel.com/privkey.pem;

    # Modern TLS settings
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 1d;
    ssl_stapling on;
    ssl_stapling_verify on;

    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Frame-Options DENY always;
    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy strict-origin-when-cross-origin always;
    server_tokens off;

    # Logging
    access_log /var/log/nginx/stayflow-access.log;
    error_log  /var/log/nginx/stayflow-error.log;

    # Serve React SPA static files
    root /opt/stayflow/frontend;
    index index.html;

    # Forward API requests to Kestrel
    location /api/ {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
        proxy_send_timeout 60s;
    }

    location /healthz {
        proxy_pass         http://127.0.0.1:5000;
        proxy_set_header   Host            $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        access_log off;
    }

    # SPA fallback — all unknown paths return index.html
    location / {
        try_files $uri $uri/ /index.html;
    }

    # Compression
    gzip on;
    gzip_types text/plain text/css application/javascript application/json image/svg+xml;
    gzip_min_length 1024;
}
```

Enable the site:

```bash
sudo ln -s /etc/nginx/sites-available/stayflow /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

### Configure per-IP rate limiting

```bash
# /etc/stayflow/environment — add this line
ReverseProxy__KnownProxies__0=127.0.0.1
```

```bash
sudo systemctl restart stayflow-api
```

---

## Option C — Self-signed certificate (internal/intranet only)

For hotels that run StayFlow on a private LAN with no public domain,
a self-signed certificate is acceptable. It will trigger browser
warnings for staff unless the cert is added to the hotel's trusted
store via group policy or manual import.

Generate a 2-year cert:

```bash
sudo openssl req -x509 -newkey rsa:4096 -sha256 -days 730 -nodes \
  -keyout /etc/ssl/stayflow/server.key \
  -out /etc/ssl/stayflow/server.crt \
  -subj "/CN=stayflow.local" \
  -addext "subjectAltName=DNS:stayflow.local,IP:192.168.1.100"
```

Replace `/CN=` and the SAN values with your actual hostname and server IP.

Use the cert paths in the nginx or Caddy configuration above, replacing
the Let's Encrypt paths.

---

## Verifying TLS

After setup, verify from a separate machine or using `curl`:

```bash
# Check certificate validity
curl -v https://pms.yourhotel.com/healthz 2>&1 | grep -E 'SSL|TLS|expire|issuer|status'

# Check HTTP redirects to HTTPS
curl -I http://pms.yourhotel.com/healthz

# Check security headers
curl -I https://pms.yourhotel.com/healthz | grep -E 'Strict|X-Frame|X-Content|Referrer'
```

External verification: [SSL Labs](https://www.ssllabs.com/ssltest/) (for internet-accessible deployments).

---

## Production Docker Compose (all-in-one)

For hotels that prefer Docker over bare-metal service management, a
production Compose file is provided at
`deploy/docker/docker-compose.production.yml`.

It includes PostgreSQL, the StayFlow API, and a Caddy reverse proxy with
automatic TLS. See that file for setup instructions.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Browser shows "Not Secure" | HTTP port 443 not open | Open port 443 in firewall |
| `502 Bad Gateway` | Kestrel not running | `systemctl status stayflow-api` |
| Certificate expired | Auto-renewal failed | `sudo certbot renew` / check cron |
| `Connection refused` on port 5000 | Kestrel bound to wrong address | Ensure `ASPNETCORE_URLS=http://+:5000` |
| Rate limiter uses proxy IP, not client IP | `KnownProxies` not set | Add `ReverseProxy__KnownProxies__0=127.0.0.1` |
| Staff browser shows cert warning (self-signed) | Cert not trusted | Import cert into browser/OS trust store |
