# OWASP ZAP Security Scanning for Clickra

## Overview

This directory contains OWASP ZAP (Zed Attack Proxy) configuration for security
scanning Clickra's external API communication endpoints.

Since Clickra is a Windows desktop application (not a web app), ZAP is used here
to scan the HTTP endpoints the app communicates with — primarily translation
APIs and file download services.

## Files

| File | Purpose |
|------|---------|
| `zap-rules.tsv` | Scan rules — which alerts to IGNORE vs KEEP |
| `zap-false-positives.md` | Track known false positives |
| `README.md` | This documentation |

## How It Works

The ZAP scan runs via GitHub Actions (`.github/workflows/zap-security-scan.yml`)
using `zaproxy/action-baseline@v0.15.0`. The action:

1. Starts a ZAP Docker container
2. Spiders the target URL (depth 0 — API endpoint only)
3. Runs passive scan rules against the response
4. Reports findings as a GitHub Actions artifact

### Scan Targets

| Endpoint | Purpose | Scan Type |
|----------|---------|-----------|
| `translate.google.com` | Google Free Translator API | Passive |
| `api.mymemory.translated.net` | MyMemory Translation API | Passive |
| `download.documentfoundation.org` | LibreOffice download server | Passive |

## Rules Format

The rules file (`zap-rules.tsv`) uses ZAP's standard format:

```
<ruleId> IGNORE (<ruleName>)
<ruleId> FAIL (<ruleName>)
```

- **IGNORE** — suppress alert from scan results
- **FAIL** — fail the scan if this alert is found (default behavior)

### Disabled Rules (Desktop App Context)

These rules are disabled because Clickra is a desktop app, not a web application:

- **10202** — Absence of Anti-CSRF Tokens (no web forms)
- **10098** — Cross-domain JavaScript source inclusion (external APIs expected)
- **10023** — Information disclosure - Debug errors (API responses may contain debug info)
- **40012** — Cross-site scripting - Reflected (JSON responses, not browser DOM)
- **40018** — SQL Injection (external APIs use query parameters, not SQL)
- **90033** — Loosely scoped cookie (desktop app cookie handling)
- And more — see `zap-rules.tsv` for the full list

## CI Integration

The scan runs automatically when translation-related code changes:

- **PR** — baseline scan on every PR touching translation code
- **Push to main** — baseline scan on push
- **Manual** — trigger via GitHub Actions workflow_dispatch

The workflow is at `.github/workflows/zap-security-scan.yml`.

## Adding New Rules

To disable a new rule, add a line to `zap-rules.tsv`:

```
<ruleId> IGNORE (<ruleName>)
```

To make a scan fail on a specific rule, use `FAIL` instead of `IGNORE`.

## False Positive Tracking

Document known false positives in `zap-false-positives.md` with:
- Rule ID and name
- Affected endpoint
- Reason it's a false positive
- Date documented
