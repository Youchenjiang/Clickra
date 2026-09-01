# Phase 0 — Store Permission Request Preparation

> **Status**: ⏳ PENDING MICROSOFT WRITTEN APPROVAL  
> **Date**: 2026-08-31  
> **Related**: F1-13 Store-Resilient Optional Fluent Delivery

---

## 1. Overview

Phase 0 is the hard gate for F1-13. We needed to verify the Store account supports:

1. **Optional packages** — Deliver Fluent UI as an optional add-on
2. **Related sets** — Bind main + optional packages for version sync
3. **Executable code in optional package** — Fluent.exe runs as full-trust app
4. **Activatable optional app** — Optional app starts via AUMID from launcher

**Current evidence**: Partner Center shows the 「附加元件」(Add-ons) page with a 「建立新附加元件」button. This confirms ordinary add-on UI access only. It does **not** establish Store submission permission for optional packages, related sets, or executable optional packages. Microsoft documentation explicitly requires Windows Developer Support permission for Store submissions using optional packages and/or related sets.

**Current result**: Phase 0 remains pending until Microsoft provides product-specific written approval for Clickra Store product `9NGLBF6P1KLD` and the requested architecture.

**Exit condition**: If denied or account ineligible, F1-13 stops immediately. No workaround with two Store listings.

---

## 2. Verification Checklist

### Account & Product Readiness
- [x] Store account is in good standing (no policy violations)
- [x] Clickra product exists in Partner Center (ID: 9NGLBF6P1KLD)
- [x] Current main MSIX is published and passing certification
- [x] Publisher identity: `CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138`
- [x] Partner Center 「附加元件」page accessible with 「建立新附加元件」button visible

### Technical Context
- Current architecture: Single MSIX with NativeAOT shell + WinUI 3 dashboard
- Target architecture: NativeAOT main (zero dependency) + WinUI 3 optional (carries Windows App Runtime)
- Package family name: Same publisher, main = `Clickra`, optional = `Clickra.Fluent`
- Windows App Runtime 2.x dependency: Only in optional package

### Documentation
- [x] `docs/development/store_optional_fluent_plan.md` — Full technical plan
- [x] `docs/development/store_submission_guide.md` — Post-approval submission guide
- [x] This document — Phase 0 verification record

---

## 3. Permission Request — REQUIRED

Submit the prepared request in `docs/development/phase0_support_ticket.md` through Windows Developer Support or the Windows apps support entry in Partner Center.

The following was verified on 2026-08-31 by navigating to:
`Partner Center > 應用程式與遊戲 > Clickra > 附加元件`

The button is visible and functional. Record this as supporting account evidence, not as permission approval.

Microsoft's optional-package documentation states that Store submissions using optional packages and/or related sets require permission. The support response must identify the Clickra product and confirm the approved scope; a generic documentation link or confirmation of ordinary add-on access is not sufficient.

---

## 4. Partner Center Verification — Partially Completed

### Step 1: Add-on Page Access ✅
1. Go to Partner Center → 應用程式與遊戲 → Clickra
2. Navigate to 附加元件 (Add-ons)
3. **Result**: Page loads successfully, 「建立新附加元件」button is visible
4. Currently shows 0 add-ons

### Step 2: Pending — Explore Add-on Creation Form Without Publishing
The next step is to click 「建立新附加元件」 and verify:
- What product types are available (Optional package? DLC? In-app product?)
- Whether MainPackageDependency can be set
- Whether related set configuration is available
- Any limitations or warnings

> **Note**: Screenshots should be taken of each step for documentation.

Do not create, submit, or publish an add-on merely to probe permissions. Opening and inspecting the form is read-only discovery; any Partner Center creation or submission requires explicit authorization and should wait for the support response if the product type is ambiguous.

---

## 5. Exit Conditions

| Outcome | Action |
|---------|--------|
| ✅ Partner Center add-on page accessible | Record supporting evidence; Phase 0 remains pending |
| ✅ Microsoft gives product-specific written approval | **Phase 0 PASSED** — Proceed to Phase 1 |
| ⚠️ Add-on page accessible but creation fails | Investigate specific error |
| ❌ Add-on page not available | Contact Windows Developer Support |
| ❌ Account ineligible | **STOP F1-13** — Fallback to GitHub dual-track |

**Current result**: ⏳ Partner Center add-on UI is accessible, but Microsoft written approval is pending. Phase 0 has not passed.

---

## 6. Results Log

### Support Ticket
- **Ticket ID**: _pending_
- **Submitted**: _pending_
- **Response received**: N/A
- **Support team / responder**: _pending_
- **Approved capability scope**: _pending_
- **Limitations / certification requirements**: _pending_

### Partner Center Verification
- [x] 附加元件 page accessible: ✅ Yes
- [x] 「建立新附加元件」button visible: ✅ Yes
- [ ] Add-on creation form explored: _pending_
- [ ] Optional package type available: _pending_
- [ ] MainPackageDependency configurable: _pending_

### Decision
- **Outcome**: ⏳ Phase 0 pending Microsoft written approval
- **Partner Center add-on UI access**: Confirmed (2026-08-31); not treated as optional-package permission
- **Next step**: Submit the support request, save the ticket ID and full response, then evaluate whether the reply satisfies every approval criterion

---

## 7. Approval Acceptance Criteria

Phase 0 passes only if the written response is attributable to Clickra Store product `9NGLBF6P1KLD` and permits all required Store submission elements:

1. Optional package submission.
2. Related-set submission.
3. Executable code in the optional package.
4. A full-trust packaged WinUI 3 executable in that optional package.
5. AUMID activation of the optional application, or a documented supported alternative preserving the same fallback model.
6. A Partner Center product/submission route that can be exercised in a private flight.

If the response is generic, incomplete, or only confirms ordinary Add-ons-page access, reply on the same ticket with the missing numbered questions. Do not mark Phase 0 passed.

## 8. After Approval: Phase 1 Preview

Once Phase 0 passes, Phase 1 involves building a minimal local PoC:

1. Create minimal NativeAOT main package (no Windows App Runtime dependency)
2. Create minimal WinUI 3 optional package (with Windows App Runtime dependency)
3. Add related-set bundle metadata
4. Verify main installs and runs on clean VM
5. Verify optional installs, launcher routes to Fluent
6. Verify optional removal/loss falls back to AOT

This work can begin locally without Store upload — sideloading for internal testing.

---

## 9. Contact Information

- **Microsoft Developer Support**: https://aka.ms/storesupport
- **Partner Center**: https://partner.microsoft.com/dashboard
- **Store listing**: https://apps.microsoft.com/detail/9NGLBF6P1KLD
- **Project repo**: https://github.com/YouchenJiang/Clickra
