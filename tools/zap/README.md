# Security Scanning for Clickra

## Overview

This directory contains OWASP ZAP configuration for security scanning Clickra's
external API communication endpoints, plus documentation about third-party API
security limitations.

The GitHub Actions workflow (`.github/workflows/zap-security-scan.yml`) runs two
complementary security checks:

1. **NuGet Dependency Vulnerability Scan** — scans Clickra's own package
   dependencies for known CVEs (actionable, Clickra can fix these)
2. **OWASP ZAP Baseline Scan** — scans third-party translation APIs for
   security header issues (informational only, Clickra cannot fix these)

## Why ZAP Limitations Exist

Clickra is a **Windows desktop application** with no local web server. ZAP is a
web application scanner, so it can only scan the external HTTP endpoints Clickra
communicates with. The findings are all **server-side response header issues**
on third-party APIs — Clickra is the client and cannot modify these.

**NuGet vulnerability scanning is the actionable security check** — it finds
vulnerabilities in Clickra's own dependencies that can be updated.

## Files

| File | Purpose |
|------|---------|
| `zap-rules.tsv` | ZAP scan rules — which alerts to IGNORE vs KEEP |
| `zap-false-positives.md` | Track known false positives with detailed justification |
| `README.md` | This documentation |

## ZAP Scan Target

| Endpoint | Purpose | Notes |
|----------|---------|-------|
| `translate.google.com` | Google Free Translator API | Non-official API, no security headers |

## CI Integration

The workflow runs automatically when:

- **PR** — touches any source code or ZAP config files
- **Push to main** — touches source code
- **Manual** — trigger via GitHub Actions workflow_dispatch

### NuGet Vulnerability Scan

Fails the pipeline if any package has a known CVE. This is the **primary
security gate** — Clickra controls these dependencies and can update them.

### ZAP Baseline Scan

Runs as informational only (`fail_action: false`). Reports server-side header
issues on third-party APIs that Clickra cannot fix. See `zap-false-positives.md`
for detailed justification.

## Adding New ZAP Rules

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
