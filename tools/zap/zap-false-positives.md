# ZAP False Positives — Clickra

Document known false positives from OWASP ZAP scans for Clickra.
These are findings that are expected behavior for a Windows desktop application
making external API calls, not actual security vulnerabilities.

## Format

For each false positive, document:
- **Rule ID**: ZAP rule number
- **Rule Name**: ZAP rule name
- **Affected Endpoint**: Which API endpoint triggered the finding
- **Finding**: What ZAP reported
- **Justification**: Why this is a false positive for Clickra
- **Date Documented**: When this was documented
- **Reviewed By**: Who confirmed the false positive

---

## Documented Findings (Server-Side Issues)

These findings are on **external API server response headers**. Clickra is the CLIENT — it cannot modify the server's headers. These are NOT false positives; they are real security issues on the third-party API servers that Clickra cannot control.

### Rule 10035 — Strict-Transport-Security Header Not Set

**Affected Endpoint**: `api.mymemory.translated.net`

**Finding**: Server does not set HSTS header, allowing potential downgrade attacks.

**Why Clickra Cannot Fix**: This is a server-side response header. The server (MyMemory) must set `Strict-Transport-Security` in its response. Clickra can only ensure it uses HTTPS for requests (which it already does).

**Client-Side Mitigation**: Clickra already uses HTTPS for all API calls. No HTTP fallback exists in the code.

**Date Documented**: 2026-08-24

---

### Rule 10021 — X-Content-Type-Options Header Missing

**Affected Endpoint**: `api.mymemory.translated.net`

**Finding**: Server does not set `X-Content-Type-Options: nosniff`, allowing MIME-type sniffing.

**Why Clickra Cannot Fix**: This is a server-side response header. The server must set this header to prevent browsers from MIME-sniffing responses.

**Client-Side Mitigation**: Clickra could add Content-Type validation in the HTTP client, but the current implementation already validates response structure (JSON parsing).

**Date Documented**: 2026-08-24

---

### Rule 10036 — Server Leaks Version Information

**Affected Endpoint**: `api.mymemory.translated.net`

**Finding**: Server response header contains `Server: nginx/x.x.x`, leaking version information.

**Why Clickra Cannot Fix**: This is a server-side response header. The server administrator must configure nginx to hide version information (`server_tokens off;`). Clickra cannot control this.

**Risk Assessment**: Low risk for Clickra — the leaked version info is on the third-party API server, not on Clickra's infrastructure.

**Date Documented**: 2026-08-24

---

### Rule 10063 — Permissions Policy Header Not Set

**Affected Endpoint**: `api.mymemory.translated.net`

**Finding**: Server does not set `Permissions-Policy` header.

**Why Clickra Cannot Fix**: This is a server-side response header. The server must set this header to control browser features. Clickra is a desktop app, not a browser.

**Date Documented**: 2026-08-24

---

### Rule 10038 — Content Security Policy (CSP) Header Not Set

**Affected Endpoint**: `api.mymemory.translated.net`

**Finding**: Server does not set CSP header.

**Why Clickra Cannot Fix**: This is a server-side response header. CSP is primarily for web applications to prevent XSS. Clickra is a desktop app that processes JSON responses.

**Date Documented**: 2026-08-24

---

### Rule 10098 — Cross-Domain JavaScript Source Inclusion

**Affected Endpoint**: `translate.google.com`

**Finding**: External JavaScript resource loaded from a different domain.

**Why Clickra Cannot Fix**: This is a server-side CORS policy. Google Translate API legitimately serves cross-domain resources.

**Date Documented**: 2026-08-24

---

### Rule 90005 — Sec-Fetch-* Headers Missing

**Affected Endpoint**: All external APIs

**Finding**: Browser-specific `Sec-Fetch-*` headers are missing.

**Why Clickra Cannot Fix**: These are browser-specific request headers that desktop applications do not send. Clickra is not a browser.

**Date Documented**: 2026-08-24

---

## Rules That Are False Positives (Desktop App Context)

| Rule ID | Rule Name | Reason |
|---------|-----------|--------|
| 10202 | Absence of Anti-CSRF tokens | Desktop apps don't use web forms |
| 10023 | Information disclosure - Debug errors | API responses may contain debug info |
| 10024 | Information disclosure - Sensitive data in URL | API parameters are not sensitive |
| 40012 | Cross-site scripting (Reflected) | JSON API responses, not browser DOM |
| 40014 | Cross-site scripting (DOM) | No browser DOM context |
| 40018 | SQL Injection | External APIs use query params, not SQL |
| 90033 | Loosely scoped cookie | Desktop apps manage cookies differently |
