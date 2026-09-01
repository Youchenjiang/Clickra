# Windows Developer Support — Optional Package Permission Request

> **Status**: Ready for developer review and manual submission  
> **Submit through**: [Microsoft Developer Support](https://aka.ms/storesupport) or the Windows apps support entry in Partner Center  
> **Purpose**: Obtain written, product-specific Store submission permission. Access to the Partner Center Add-ons page alone is not treated as approval for optional packages or related sets.

---

## Subject

```
Permission request: executable optional package and related set for Clickra (9NGLBF6P1KLD)
```

## Body

```
Hello Windows Developer Support,

I am the developer of Clickra, an existing Microsoft Store Windows app and context-menu file conversion utility.

- Store product ID: 9NGLBF6P1KLD
- Store listing: https://apps.microsoft.com/detail/9NGLBF6P1KLD
- Current product: one published MSIX application

I am requesting written permission for this specific Store product to submit an optional package and related set. The optional package would contain executable code: a full-trust, packaged WinUI 3 dashboard.

## Proposed architecture

Main MSIX (required and independently functional):
- ClickraLauncher.exe — NativeAOT routing entry point
- Clickra.exe — NativeAOT CLI and fallback dashboard
- ClickraShell.dll — NativeAOT Explorer context-menu extension
- No Microsoft.WindowsAppRuntime framework dependency

Optional MSIX (installed only after an explicit user action):
- Clickra.Fluent.exe — framework-dependent, full-trust packaged WinUI 3 application
- Clickra managed dependencies and WinUI resources
- Its own Microsoft.WindowsAppRuntime framework dependency
- `uap3:MainPackageDependency` referencing the Clickra main package
- Same Store publisher as the main package

The optional package would be part of the same related set as the main package. The main launcher would activate the optional application by AUMID through Windows packaged activation (`IApplicationActivationManager`). If the optional package is absent, incompatible, or fails to activate, the launcher would start the NativeAOT fallback dashboard.

The settings page would offer a user-initiated installation action using the Microsoft Store package APIs with the normal system confirmation UI. We are not requesting silent installation or the `storeOptionalPackageInstallManagement` restricted capability.

We would prefer the optional Fluent application not to create a second Start menu entry, while remaining activatable by AUMID. This is a proposed design that still requires confirmation and testing, not an asserted capability.

## Permission and implementation questions

Please confirm in writing:

1. Whether Store product 9NGLBF6P1KLD is approved to submit optional packages and related sets.
2. Whether the optional package may contain a full-trust packaged WinUI 3 executable and its Microsoft.WindowsAppRuntime dependency.
3. Whether that executable may be registered as an activatable optional application and launched by AUMID from the main package.
4. Whether the optional application can use a hidden app-list entry (for example, `AppListEntry="none"`) while remaining activatable by AUMID, or whether a visible second Start menu entry is required.
5. Which Partner Center associated-product/add-on type and submission workflow should be used for this architecture. The account already shows the Add-ons page and a Create new add-on button, but we understand that this UI access may not constitute optional-package or related-set submission approval.
6. Which related-set, bundle, identity, publisher, and versioning requirements the Store ingestion pipeline expects.
7. Whether a private flight or preliminary package review is required before production certification, and whether certification notes must identify the executable optional-package architecture.
8. Whether any additional declarations, restricted capabilities, agreements, or onboarding steps are required.

If this request has reached the wrong support category, please route it to the Windows Store package-ingestion team that handles MSIX optional packages and related sets. A documentation link alone will not establish whether this product has the required Store permission, so please record the approval status for Store product 9NGLBF6P1KLD and any applicable limitations in the response.

Thank you.
```

## Before submitting

Replace or attach only information that has been verified. Do not claim that Phase 0 passed until Microsoft replies with product-specific written approval. Save the ticket ID, submission date, complete response, approved capability scope, limitations, and responding support team in `phase0_store_permission_request.md`.
