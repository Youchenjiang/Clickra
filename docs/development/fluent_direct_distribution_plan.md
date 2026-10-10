# Standalone Fluent Direct Distribution Plan

> **Status:** fallback candidate only; not a production release path.
>
> **Trigger:** evaluate this plan if Microsoft Store Optional Package / Related Set delivery
> remains unavailable or cannot be approved for Clickra.
>
> **Current shipping truth:** Microsoft Store continues to receive the existing NativeAOT
> `Clickra.msix`. GitHub Releases do not currently publish a direct MSIX because the planned
> production signing path is not yet integrated and validated.

## 1. Purpose

This plan defines a fallback distribution model in which the Microsoft Store package remains
the small, dependency-free NativeAOT product while the Fluent WinUI 3 interface is offered as
a separately identified direct-download package from GitHub Releases or the project website.

The goal is not to recreate Optional Packages outside the Store. The goal is to preserve the
same product reliability property with a different delivery boundary:

> Fluent may fail to install, update, or launch without making the Store-installed NativeAOT
> Clickra unusable.

This plan is intentionally independent from `store_optional_fluent_plan.md`. An eventual
Microsoft policy reversal or approval can reactivate the Store Optional Package candidate
without requiring this direct-distribution design to become production.

## 2. Non-goals

This fallback does **not**:

- create a second Microsoft Store listing;
- reuse `uap3:MainPackageDependency` or a Related Set outside the Store;
- make the Store NativeAOT package download or execute an unverified external Fluent payload;
- duplicate Explorer shell registrations in the Fluent package;
- replace the Store package identity, Publisher, PFN, App ID, or Shell CLSID;
- require the Store launcher to discover or activate a separately identified Fluent package in
  the first standalone release;
- bundle the full Windows App SDK self-contained payload merely to avoid dependency handling.

## 3. Target topology

```text
Microsoft Store
┌──────────────────────────────────────────────┐
│ g1014308.Clickra                             │
│                                              │
│ ClickraLauncher.exe                          │
│ Clickra.exe            NativeAOT             │
│ ClickraShell.dll       Explorer integration  │
│                                              │
│ no Windows App Runtime dependency            │
└──────────────────────────────────────────────┘

GitHub / project website
┌──────────────────────────────────────────────┐
│ Clickra Fluent Direct                        │
│ distinct package identity                    │
│                                              │
│ Clickra.Fluent.exe      WinUI 3              │
│ Clickra.Core.dll + managed dependencies      │
│ Windows App Runtime framework dependency     │
│                                              │
│ no Explorer shell registrations              │
│ no Store MainPackageDependency               │
└──────────────────────────────────────────────┘
```

The Store package remains the sole owner of Explorer integration. The direct Fluent package
is a UI surface, not a second full Clickra installation that competes for shell ownership.

## 4. Package identity contract

The standalone Fluent package must use an identity that is deliberately distinct from the
Store package identity.

Required invariants:

1. The direct Fluent package `Identity Name` must not be `g1014308.Clickra`.
2. Its Publisher must exactly match the certificate Subject assigned by the approved direct
   production signing configuration.
3. Its package family must therefore remain separate from the Microsoft Store package family.
4. Its version must be derived from the same release version source as the corresponding
   Clickra release; no independent ad-hoc versioning is allowed.
5. The current `packaging/msix/AppxManifest.Fluent.xml` must remain the Store Optional Package
   candidate and must not be silently repurposed as the standalone direct manifest.
6. A standalone manifest should be introduced only during an implementation phase, after the
   direct signing identity is known.
7. Its application ID/AUMID must be stable across direct releases and independently verifiable;
   it must never be treated as an update to the Store application's AUMID.

The exact standalone `Identity Name` and Publisher are intentionally unresolved here. They
must not be guessed before production signing onboarding provides the required certificate
identity and the package naming decision is reviewed.

## 5. Shell and file-association ownership

The standalone Fluent package must not register any of the following:

- `windows.fileExplorerContextMenus`;
- `windows.fileExplorerClassicContextMenuHandler`;
- the existing Clickra COM server / Shell CLSID;
- the Store package's file-type associations unless a separate product decision explicitly
  proves that coexistence is safe.

The Store NativeAOT package is the single shell owner. This avoids duplicate context menus,
COM registration conflicts, ambiguous uninstall ownership, and one package removing shell
state required by the other.

If a future direct-only distribution needs Explorer integration on machines that do not have
the Store package, that is a separate product decision and must not be achieved by copying the
Store manifest extensions into the first standalone Fluent package.

The standalone Fluent package also must not install `ClickraShell.dll`. Uninstalling either
channel must never remove or invalidate shell state owned by the other channel.

## 6. Launch model

### 6.1 First standalone release

The first standalone Fluent release should launch through its own packaged application entry
and Start menu shortcut. It must not depend on the current Store `ClickraLauncher.exe`.

The existing launcher is designed around the Optional Package candidate and currently derives
an AUMID from the main package family. A separately identified direct Fluent package therefore
cannot be treated as a drop-in target for that activation path.

### 6.2 Future integrated routing

Integrated routing from Store NativeAOT to standalone Fluent is optional future work. It may
only be considered after there is an explicit discovery and activation contract that:

- identifies the exact direct Fluent package without assuming a Related Set;
- forwards Unicode, whitespace, quoting, and multi-file arguments losslessly;
- obtains a reliable activation/readiness result rather than treating short PID survival as
  proof of successful UI startup;
- fails closed to the NativeAOT UI when the direct package is absent, incompatible, or broken.

The direct Fluent package must remain useful even if integrated routing is never implemented.

## 7. Runtime and dependency model

`Clickra.Fluent` is currently a framework-dependent WinUI 3 application. A production direct
package must validate its dependency behavior on clean supported Windows systems rather than
assuming Store dependency resolution semantics apply unchanged to sideload/direct install.

Before production, verify at least:

- Windows App Runtime framework dependency installation and servicing;
- the actual .NET Desktop Runtime requirement for the published Fluent artifact;
- clean Windows 10 and Windows 11 installation behavior;
- missing-runtime failure messaging and recovery;
- architecture support for every published artifact;
- install behavior when the Store NativeAOT package is already present;
- install behavior when no Store package is present.

No production plan may require end users to import a development certificate or enable an
untrusted package path.

## 8. Shared data contract

The two applications currently share Clickra domain concepts such as settings, history, and
task records through `Clickra.Core`, but separate package identities do not by themselves prove
that all package-scoped storage APIs resolve to the same location.

The standalone design therefore distinguishes two categories:

1. **Explicit Clickra data paths** such as project-owned files under the documented Clickra
   data directory. These may be shared only after concurrent access, locking, corruption, and
   upgrade behavior are tested.
2. **Package-scoped Windows storage** such as `ApplicationData.Current`. These must be assumed
   separate between package families unless tests prove otherwise; business-critical state
   must not silently depend on them being shared.

Required coexistence tests include:

- simultaneous NativeAOT and Fluent startup;
- concurrent history writes;
- task create/update/resume/cancel behavior;
- settings updates from each UI;
- Fluent uninstall while Store NativeAOT remains installed;
- Store NativeAOT update while Fluent remains installed;
- Fluent update while Store NativeAOT remains installed.

If both channels use `%LocalAppData%\Clickra` or another shared mutable path, the production
contract must explicitly define schema/version compatibility, atomic-write behavior, lock and
watcher semantics, task-ID collision handling, mixed-version behavior, migration/rollback, and
backup policy. Neither uninstaller may delete shared data while the other channel remains
installed.

If shared mutable state cannot be made deterministic, the fallback design must prefer explicit
separation over undocumented cross-package coupling.

## 9. Signing and trust boundary

Production direct distribution remains governed by
`multi_channel_signing_distribution_plan.md` and
`signpath_foundation_application_readiness.md`.

For the standalone Fluent package specifically:

- a self-signed development certificate is local-test only;
- the production Publisher must exactly match the approved signing certificate Subject;
- the unsigned artifact must originate from the approved GitHub trusted-build path;
- every production signing request must satisfy the selected provider's approval/origin rules;
- the retrieved signed MSIX must pass independent identity, version, package-structure, and
  signature verification before publication;
- verification must assert the expected application ID/AUMID, dependency declarations, and
  absence of Explorer shell/COM registrations;
- no GitHub Release may publish an unsigned, development-signed, or unverifiable Fluent MSIX.

The current `scripts/build_github_msix.ps1` builds the NativeAOT direct package shape only. Its
existence does not mean Fluent direct packaging or signing is already implemented.

## 10. Release and update model

The Store and direct Fluent channels are siblings, not replacement artifacts for each other.

For a given product release:

- Store publication continues to use the unchanged Store identity and NativeAOT artifact;
- a direct Fluent artifact may be published only when its own signing and verification gates
  pass;
- failure or delay in direct Fluent signing must not block an otherwise valid Store release;
- failure or delay in Store publication must not cause a direct artifact to be substituted into
  the Store submission path;
- GitHub Release notes and filenames must clearly distinguish Store archival artifacts from
  direct Fluent artifacts.

The production direct Fluent asset should use an unambiguous filename such as
`Clickra-fluent-direct.msix`; it must not reuse `Clickra.msix` or `Clickra-store.msix`.

Automatic cross-package replacement is not part of this design. The standalone Fluent package
has its own servicing lifecycle and must be tested for forward update and uninstall independently.

## 11. Required validation matrix

### Packaging

- standalone manifest contains no `MainPackageDependency`;
- standalone identity is distinct from the Store identity;
- standalone package contains no Explorer shell registration or existing Shell CLSID;
- Windows App Runtime dependency is present as intended;
- package version matches the release version source;
- Publisher matches the production signing certificate Subject.

### Installation / coexistence

- clean Windows 10: install, launch, update, uninstall;
- clean Windows 11: install, launch, update, uninstall;
- Store NativeAOT installed first, then Fluent direct;
- Fluent direct installed first, then Store NativeAOT;
- uninstall Fluent while Store NativeAOT remains functional;
- uninstall Store NativeAOT while Fluent remains functional as a standalone UI;
- update either package while the other remains installed;
- mixed-version coexistence, including older Fluent with newer Store NativeAOT and the reverse;
- repair/rollback of either channel without corrupting the other;
- Explorer shows exactly one Clickra shell integration when Store NativeAOT is installed.

### Functional parity smoke tests

- dashboard launch;
- at least one Office conversion;
- at least one PDF conversion including password/input flow where applicable;
- at least one image conversion;
- cancellation and progress;
- history and output-folder actions;
- task resume/parking where supported.
- quoted/Unicode/multi-file paths for any future cross-package or CLI forwarding contract.

### Failure behavior

- missing Windows App Runtime;
- missing required .NET runtime;
- corrupted or failed Fluent update;
- Fluent executable exits during startup;
- Store NativeAOT remains usable after every Fluent failure case.

## 12. Implementation phases

### Phase 0 — Policy and signing prerequisites

- [ ] Microsoft SR 2608310060003141 reaches an authoritative Store policy conclusion.
- [ ] Direct production signing onboarding provides the exact certificate Subject/Publisher and
      required trusted-build identifiers.
- [ ] The maintainer explicitly approves implementation of the standalone fallback.

No production code or manifest identity should be changed merely because this plan exists.

### Phase 1 — Standalone packaging PoC

- [ ] Introduce a dedicated standalone Fluent manifest rather than modifying the Optional
      Package manifest in place.
- [ ] Introduce a reproducible standalone Fluent packaging command/script.
- [ ] Produce a development-signed local MSIX for controlled testing only.
- [ ] Prove the package installs and launches without `MainPackageDependency`.
- [ ] Prove that it does not register Explorer shell integration.

### Phase 2 — Coexistence and data validation

- [ ] Run the full install/update/uninstall matrix with Store NativeAOT present and absent.
- [ ] Validate settings/history/task data behavior under separate package identities.
- [ ] Resolve any required explicit shared-data contract without depending on package-family
      coincidence.

### Phase 3 — Production signing integration

- [ ] Build the exact unsigned Fluent artifact on the approved trusted build.
- [ ] Submit it through the approved production signing configuration.
- [ ] Independently verify identity, version, signer, and package structure.
- [ ] Fail closed on any mismatch.

### Phase 4 — Direct publication

- [ ] Attach the verified standalone Fluent asset to the matching GitHub Release.
- [ ] Document runtime prerequisites, install/uninstall steps, and channel differences.
- [ ] Update the project website/download surface only after the signed artifact is public.
- [ ] Keep Store NativeAOT release automation independent.

## 13. Exit conditions

Stop this fallback before production if any of the following is true:

- Microsoft restores/approves the Optional Package design and the maintainer chooses that path
  instead;
- a publicly trusted production signing path is unavailable;
- a standalone Fluent package cannot coexist with the Store NativeAOT package without shell,
  data, or servicing conflicts;
- required runtime dependencies cannot be delivered with an acceptable supported user
  experience;
- the implementation would require the Store package to bypass Store servicing by downloading
  and executing arbitrary external binaries.

## 14. Relationship to existing documents

- `store_optional_fluent_plan.md` owns the Store Optional Package / Related Set candidate.
- `multi_channel_signing_distribution_plan.md` owns trust, signing, and channel isolation.
- `signpath_foundation_application_readiness.md` owns SignPath onboarding readiness.
- `CI_CD_DUAL_RELEASE_GUIDE.md` describes the live automated release pipeline and remains the
  authority for what is actually shipped today.
- `archive/dual_track_guide.md` is historical evidence only. Its same-identity replacement model
  must not be revived implicitly by this plan.

This document becomes a production authority only after an explicit adoption decision and
implementation/validation work. Until then it is a fallback design specification.
