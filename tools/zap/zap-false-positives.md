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

## Documented Findings (Server-Side / Not Actionable by Clickra)

These findings are on **external API server response headers**. Clickra is the CLIENT — it cannot modify the server's headers. They are included here for completeness so future contributors understand why ZAP alerts exist but are not addressed in Clickra code.

> **Note**: The current ZAP workflow only scans `translate.google.com`. The MyMemory findings below are from earlier manual scans and would appear if MyMemory were added to the scan target.

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

---

## Current API Security Limitations

### Google Free Translator (`translate.google.com/translate_a/t`)

| 項目 | 狀態 | 說明 |
|------|------|------|
| HTTPS | ✅ 已使用 | Clickra 透過 HTTPS 連線 |
| HSTS | ❌ 缺少 | Server 未設定 Strict-Transport-Security |
| X-Content-Type-Options | ❌ 缺少 | Server 未設定 nosniff |
| CSP | ❌ 缺少 | Server 未設定 Content-Security-Policy |
| API 類型 | ⚠️ 非官方 | 這是 Google 未公開的內部 API，隨時可能變更或停止 |
| 隱私 | ⚠️ 資料傳送至 Google | 翻譯內容會傳送至 Google 伺服器 |

### MyMemory (`api.mymemory.translated.net`)

| 項目 | 狀態 | 說明 |
|------|------|------|
| HTTPS | ✅ 已使用 | Clickra 透過 HTTPS 連線 |
| HSTS | ❌ 缺少 | Server 未設定 Strict-Transport-Security |
| X-Content-Type-Options | ❌ 缺少 | Server 未設定 nosniff |
| Server 版本洩漏 | ⚠️ 泄漏 | Server header 包含 nginx 版本號 |
| Permissions Policy | ❌ 缺少 | Server 未設定 Permissions-Policy |
| CSP | ❌ 缺少 | Server 未設定 Content-Security-Policy |
| 速率限制 | ⚠️ 有 | 免費版有嚴格的速率限制 |
| 隱私 | ⚠️ 資料傳送至 MyMemory | 翻譯內容會傳送至第三方伺服器 |

### Client-Side Mitigations (Clickra 已實施)

| Mitigation | 說明 |
|------------|------|
| 強制 HTTPS | 所有 API 請求都使用 https://，無 HTTP fallback |
| Response 驗證 | 解析 JSON 回應結構，確保格式正確 |
| 重試機制 | 失敗時自動重試，避免單點故障 |
| 速率控制 | 內建延遲和並發控制，避免觸發 API 限制 |

---

## Roadmap: Secure Translation API

**目標**: 將翻譯 API 遷移至更安全的替代方案

**優先級**: 高

**預計時間**: v3.7.0

### 選擇方案

| 方案 | 安全性 | 免費額度 | 優點 | 缺點 |
|------|--------|----------|------|------|
| DeepL API Free | ✅ 高 | 每月 50 萬字 | 翻譯品質最好、有完整安全 headers | 需註冊 API key |
| LibreTranslate (自架) | ✅ 最高 | 無限 | 完全控制、無隱私疑慮 | 需要伺服器、維護成本 |
| Microsoft Translator | ✅ 高 | 每月 200 萬字 | 免費額度高、安全性好 | 需註冊 API key |
| Google Cloud Translation | ✅ 高 | 每月 50 萬字 | 官方 API、穩定 | 需註冊 API key、計費 |

### 實施計畫

1. **Phase 1**: 新增 DeepL API 翻譯引擎實作
2. **Phase 2**: 保留 Google Free 和 MyMemory 作為 fallback
3. **Phase 3**: 在設定中讓使用者選擇翻譯引擎
4. **Phase 4**: 未來版本預設使用 DeepL，其他作為備選

### 安全性改善

| 改善項目 | 說明 |
|----------|------|
| HSTS | DeepL、Microsoft、Google Cloud 都有設定 |
| X-Content-Type-Options | DeepL、Microsoft、Google Cloud 都有設定 |
| API 金鑰管理 | 使用者自己的 API key，不會共用 |
| 資料隱私 | 官方 API 有明確的隱私政策 |

### 風險評估

| 風險 | 影響 | 緩解措施 |
|------|------|----------|
| API key 洩漏 | 高 | 使用者自己的 key，不硬編碼 |
| API 變更 | 中 | 保留多個翻譯引擎作為 fallback |
| 速率限制 | 中 | 實作智慧速率控制和重試 |
| 成本 | 低 | 免費額度對一般使用者足夠 |
