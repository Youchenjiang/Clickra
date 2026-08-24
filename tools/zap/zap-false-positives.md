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

## No False Positives Documented Yet

When running your first ZAP scan, add findings here with the format above.

Example entry:

```markdown
### Rule 10098 — Cross-domain JavaScript source inclusion

**Affected Endpoint**: `https://translate.google.com/translate_a/t`

**Finding**: External JavaScript resource loaded from a different domain.

**Justification**: Clickra communicates with Google Translate API which
legitimately serves JavaScript resources. This is expected behavior for
the Google Translate API.

**Date Documented**: 2026-08-24

**Reviewed By**: [Your Name]
```

---

## Common False Positives for Desktop Apps

These are typical false positives when scanning desktop app HTTP traffic:

| Rule ID | Rule Name | Common False Positive Reason |
|---------|-----------|------------------------------|
| 10098 | Cross-domain JavaScript source inclusion | External APIs serve JS resources |
| 10202 | Absence of Anti-CSRF tokens | Desktop apps don't use web forms |
| 10023 | Information disclosure - Debug errors | API responses may contain debug info |
| 10024 | Information disclosure - Sensitive data in URL | API parameters are not sensitive |
| 40012 | Cross-site scripting (Reflected) | JSON API responses, not browser DOM |
| 40014 | Cross-site scripting (DOM) | No browser DOM context |
| 40018 | SQL Injection | External APIs use query params, not SQL |
| 90033 | Loosely scoped cookie | Desktop apps manage cookies differently |
