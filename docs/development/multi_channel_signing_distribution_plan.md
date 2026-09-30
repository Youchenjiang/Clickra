# Clickra Multi-Channel Signing and Distribution Plan

## 1. Purpose

This document defines the target signing and distribution architecture for Clickra across three independent channels:

1. local development and testing;
2. GitHub direct-download releases;
3. Microsoft Store distribution and post-publication archival.

The three channels intentionally use different trust and package-identity models. They must remain operationally independent so that a failure or policy change in one channel cannot silently alter another channel's package identity, signing behavior, or publication state.

This plan also establishes a hard governance rule: a capability requirement such as "publicly trusted code signing" does not itself select a provider. Provider-specific integration is allowed only after the provider has been explicitly selected.

---

## 2. Target Architecture

### 2.1 Local development and test builds

**Signing model:** Clickra self-signed development certificate.

**Purpose:**

- local NativeAOT/MSIX development;
- packaging validation;
- automated and manual test installation;
- developer machines and controlled test machines where the Clickra development certificate can be trusted explicitly.

**Requirements:**

- keep the existing Store package identity in tracked source manifests;
- continue to support development certificate generation when the local certificate is missing or mismatched;
- never reuse a development private key for a public production distribution channel;
- development signing must never be interpreted as evidence that a GitHub Release artifact is publicly trusted.

### 2.2 GitHub direct-download release

**Signing model:** SignPath Foundation Open Source Code Signing.

**Purpose:**

- provide a publicly trusted direct-download Clickra package on GitHub Releases;
- avoid requiring end users to import a Clickra self-signed certificate;
- retain verifiable provenance from the Clickra GitHub repository and GitHub Actions build.

**High-level flow:**

```text
release tag
  -> GitHub-hosted release build
  -> build provider-neutral unsigned direct MSIX
  -> upload unsigned artifact to GitHub Actions
  -> submit signing request to SignPath
  -> SignPath trusted-build/origin verification
  -> release-signing approval/policy
  -> retrieve signed direct MSIX
  -> strict local verification
  -> attach verified direct MSIX to GitHub Release
```

**SignPath-specific prerequisites:**

- Clickra must be accepted for SignPath Foundation OSS signing;
- the SignPath GitHub trusted build integration must be configured for the Clickra repository;
- release-signing must use origin verification;
- all workflow jobs leading to the signing request must use GitHub-hosted runners as required by the OSS trusted-build policy;
- repository and SignPath accounts used in the signing chain must satisfy the Foundation's MFA and project-governance requirements;
- a project code-signing policy must be documented as required by SignPath Foundation;
- signing request approval responsibilities must be explicitly assigned;
- every production release signing request must receive the manual approval required by the SignPath Foundation OSS policy.

**Direct package identity:**

- the direct-download MSIX Publisher must exactly match the signing certificate Subject used by the approved SignPath signing configuration;
- `scripts/build_github_msix.ps1` remains the provider-neutral mechanism that temporarily substitutes the direct-distribution Publisher, rebuilds the unsigned MSIX, and restores the Store identity source files;
- the tracked source manifests remain Store-owned and must never be permanently rewritten to the direct Publisher;
- the direct package and Store package are expected to belong to different package families when their Publishers differ.

**Verification gate before publication:**

The signed direct artifact must not be attached to a GitHub Release until all of the following pass:

- MSIX exists and is structurally readable;
- `AppxManifest.xml` identity is the expected direct identity;
- package version exactly matches the release tag version;
- signer certificate Subject exactly matches the package Publisher;
- Authenticode/code-signing verification succeeds under the intended production trust model;
- architecture/package type is the intended Clickra direct package;
- artifact comes from the exact tagged GitHub workflow execution intended for the release.

Any failure is fail-closed: the direct MSIX is not published.

### 2.3 Microsoft Store distribution

**Signing model:** existing Store submission identity, followed by Microsoft Store certification and Microsoft re-signing/distribution.

**Purpose:**

- preserve the existing Store publication flow;
- retain the Microsoft Store package family and Store-owned optional-package relationship;
- allow Microsoft Store to remain operationally independent from GitHub direct signing.

**Requirements:**

- `build-msix` continues to produce the Store-identity artifact used by `store-publish`;
- Store submission must never consume the SignPath direct-download artifact;
- Store Publisher and Store package identity remain unchanged in tracked manifests;
- Store Fluent optional-package behavior continues to be gated by the exact Store Publisher/runtime package identity.

---

## 3. Post-Store-Publication GitHub Release Enrichment

After Microsoft Store certification is complete and the exact Clickra version is publicly distributed by the Store, the same GitHub Release may be enriched with the package actually distributed through the Microsoft Store channel.

### 3.1 Store acquisition

The production reconciliation path uses Microsoft Store endpoints directly. It queries the Microsoft Store display catalog for the Clickra Product ID, resolves fulfillment through Microsoft's FE3 delivery service, and downloads package bytes only from `*.delivery.mp.microsoft.com`.

The FE3 protocol support used by Clickra is vendored as MPL-2.0 source under `third_party/microsoft-store-package-downloader/`, pinned to a reviewed upstream commit and accompanied by its corresponding source and license notices. Production CI does not clone or execute a moving external repository.

### 3.2 Trust boundary

The Store catalog and FE3 acquisition layer answer the question:

> "Where can this Store package be downloaded?"

They do **not** answer:

> "Is this the correct Clickra package and may it be published?"

The second question is answered only by strict validation of the downloaded package itself.

### 3.3 Required validation before attaching a Store artifact

The Store artifact must not be attached to GitHub Release until all of the following are true:

1. **Release version match**
   - package version exactly equals the GitHub Release/tag version;
   - do not select "latest" without an exact version comparison.

2. **Store identity match**
   - package Identity Name matches the known Clickra Store identity;
   - Publisher matches the tracked Clickra Store Publisher;
   - package family is the expected Store package family.

3. **Package type and architecture match**
   - select the intended main package or bundle rather than a dependency/resource package;
   - architecture must be appropriate for the Clickra release artifact being exposed;
   - framework/resource packages returned by the resolver must never be mistaken for the main Clickra artifact.

4. **Signature verification**
   - Authenticode/package signature must validate successfully;
   - the artifact must show the expected Microsoft Store distribution signing state;
   - verification must be performed on the downloaded bytes, independent of the resolver's metadata.

5. **Source URL restrictions**
   - the final artifact download must come from an expected Microsoft-hosted Store/CDN endpoint;
   - redirects must be validated rather than blindly followed into arbitrary third-party hosts.

6. **Cross-channel version consistency**
   - the Store package must correspond to the same Clickra application version as the GitHub Release;
   - a newer Store package must never be attached to an older GitHub Release simply because it is the newest package returned by the resolver.

If any validation cannot be proven, the automation must stop without modifying the GitHub Release.

---

## 4. Release Asset Naming

The GitHub Release must clearly distinguish the direct-distribution and Store-distribution artifacts.

Proposed naming:

```text
Clickra-direct.msix
Clickra-store.msix
```

If the Store distributes a bundle instead of a single MSIX:

```text
Clickra-direct.msix
Clickra-store.msixbundle
```

The exact original Store filename and SHA-256 should also be recorded in the workflow evidence/logs.

The names must not imply that the two packages are interchangeable package families.

---

## 5. Release Lifecycle

### Phase A - Tag publication

On an explicitly authorized release tag:

1. Build and validate the normal Store-identity package.
2. Upload the Store build artifact for the independent `store-publish` job.
3. Build the direct-download package with the approved SignPath signing Publisher.
4. Submit the direct artifact to SignPath through the approved GitHub trusted-build workflow.
5. Wait for the designated approver to manually approve the SignPath release-signing request and for all configured signing/origin policies to pass.
6. Retrieve the signed artifact.
7. Run strict direct-package identity/version/signature verification.
8. Create the GitHub Release and attach only the verified SignPath-signed direct artifact.
9. Submit the Store artifact through the existing Store publication path.

The GitHub direct release and Store submission are sibling publication paths. A Store delay must not cause the already verified direct artifact to be replaced by a different package, and a SignPath failure must not silently substitute the Store/dev artifact into the direct-download slot.

### Phase B - Store publication detection

After Store submission:

1. Observe the Store submission until the exact release version is genuinely published.
2. Do not use transient submission states as proof that the package is publicly available.
3. Once published, query the Microsoft Store display catalog using the Clickra Store Product ID.
4. Resolve package fulfillment through Microsoft's FE3 delivery service.
5. Select the package only by exact Clickra identity/version/package-type rules.
6. Download only from the constrained Microsoft delivery domain.
7. Run the complete Store artifact verification gate described above.
8. Only after verification, attach the Store-distributed artifact to the existing GitHub Release for the same version.
9. Update release notes to indicate that the Microsoft Store version is available and identify the Store artifact separately from the direct artifact.

### Phase C - Failure/retry behavior

The Store enrichment workflow must be idempotent.

- If the Store version is not published yet: make no GitHub Release changes.
- If Microsoft Store catalog or FE3 acquisition is unavailable: make no GitHub Release changes.
- If no exact-version candidate exists: make no GitHub Release changes.
- If package verification fails: make no GitHub Release changes and surface the evidence.
- If the exact verified Store asset is already attached: report success without uploading a duplicate.
- If an asset with the intended Store filename exists but has a different hash: fail closed and require manual review; never overwrite it automatically.

---

## 6. Security and Trust Model

### 6.1 Trust anchors by channel

| Channel | Signer / trust source | Private-key owner | Publication trust decision |
| --- | --- | --- | --- |
| Local development | Clickra self-signed dev certificate | local developer/test environment | explicit local trust |
| GitHub direct | SignPath Foundation signing path | SignPath/HSM-backed service | SignPath policy + Clickra post-sign verification |
| Microsoft Store | Microsoft Store distribution signing | Microsoft | Store certification + downloaded package verification |

### 6.2 Explicitly prohibited shortcuts

- Do not publish the development/self-signed Store build as the GitHub production direct artifact.
- Do not store a production signing private key in Git history.
- Do not permanently rewrite the tracked Store Publisher to match a direct signing certificate.
- Do not treat a successful resolver response as proof of package identity.
- Do not choose the newest Store file without an exact version comparison.
- Do not attach unsigned or signature-invalid packages to a GitHub Release.
- Do not assume a named signing provider from a generic signing requirement.

---

## 7. Provider and External-Service Governance

The external services intentionally selected by this plan are:

1. **SignPath Foundation / SignPath.io**
   - role: GitHub direct-release production code signing;
   - scope: direct artifact only;
   - must not alter the Store package path.

2. **Microsoft Store / Microsoft delivery services**
   - role: Store submission, certification, display catalog discovery, FE3 fulfillment, Store distribution, and authoritative artifact hosting for the Store-distributed package;
   - production package acquisition is constrained to Microsoft Store catalog/FE3 endpoints and `*.delivery.mp.microsoft.com` package URLs.

Any replacement or additional provider requires a new explicit provider-selection decision before implementation.

---

## 8. Implementation Phases

### Phase 0 - Correct the current provider-specific regression

- remove the accidental Azure Artifact Signing integration from the release workflow and documentation;
- retain the provider-neutral direct-build and strict verifier scripts;
- retain Store independence;
- retain/enforce provider-selection governance.

This phase must not invent a temporary signer.

### Phase 1 - SignPath readiness and application

- verify Clickra satisfies current SignPath Foundation OSS eligibility requirements;
- add the required public code-signing policy documentation;
- define project roles for signing approval;
- prepare and submit the SignPath Foundation application;
- do not modify the production GitHub signing workflow until the project/certificate/trusted-build configuration is concretely known.

### Phase 2 - SignPath direct-release integration

- configure the SignPath GitHub trusted build system;
- configure project/artifact/signing policies;
- add only the exact required repository secret(s)/identifiers;
- update the GitHub release job to build the provider-neutral unsigned direct MSIX;
- submit the GitHub workflow artifact for signing;
- retrieve and verify the signed artifact;
- publish `Clickra-direct.msix` only after all verification gates pass.

### Phase 3 - Store artifact resolver prototype

- implement a read-only resolver client for `store.rg-adguard.net`;
- record the raw candidate list for review;
- prove exact matching for Clickra Identity Name, version, package type, architecture, and Retail channel;
- prove that final downloads come from an expected Microsoft-hosted endpoint;
- do not mutate a GitHub Release during this phase.

This prototype established the package-selection and Microsoft-delivery trust boundaries, but it was later superseded for production by direct Microsoft Store catalog/FE3 acquisition after hosted-runner qualification exposed resolver/CDN transport failures. `store.rg-adguard.net` is no longer a production dependency.

#### 2026-09-30 prototype observation

The read-only prototype in `scripts/resolve_store_package.py` successfully identified the exact published Clickra Store candidate for version `3.11.0.0` without downloading package bytes:

- package: `g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.msix`;
- package type: single `.msix` rather than a bundle;
- architecture marker: `neutral`;
- package CDN host observed: `tlu.dl.delivery.mp.microsoft.com`;
- BlockMap CDN host observed in the same resolver response: `dl.delivery.mp.microsoft.com`.

The resolver currently returned these candidate URLs with the `http://` scheme. Microsoft documents `*.dl.delivery.mp.microsoft.com` and `*.delivery.mp.microsoft.com` as Windows Update / Microsoft Store delivery endpoints that may use HTTP as well as HTTPS. The prototype therefore permits the resolver-provided HTTP scheme only when the host is inside the expected Microsoft delivery domain. HTTP is not accepted for arbitrary hosts, redirects must remain inside the same Microsoft delivery trust boundary, and downloaded bytes are not trusted until package signature and identity validation pass.

The prototype originally carried resolver-reported SHA-1 metadata into Phase 4. The production path now replaces that third-party metadata dependency with acquisition-side byte hashes from the Microsoft Store path and independently recomputes them in the verifier; identity, version, package family, and Microsoft Marketplace signature checks remain mandatory.

### Phase 4 - Store package verification

- download a known already-published Clickra Store version;
- inspect manifest identity/version/package family;
- inspect package/signature state;
- establish the exact verification assertions required for automation;
- verify behavior for bundles, resource packages, dependencies, and multiple architectures if returned.

#### 2026-09-30 verification evidence

The exact published `3.11.0.0` candidate was originally downloaded from the Microsoft delivery host returned by the resolver and verified offline without installation or execution. The later Microsoft Store catalog/FE3 hosted-runner qualification reproduced the same package bytes and all of the same verifier evidence without using the resolver in production.

- historical resolver-reported SHA-1: `ED11280D94470C24DEC86676C5CF286A60A541DF`;
- downloaded SHA-1: `ED11280D94470C24DEC86676C5CF286A60A541DF`;
- downloaded SHA-256: `23BC746154BFBDE12463B2B3109205EDBBCD8619398BAD0EBF0919C4E607D3CF`;
- manifest identity: `g1014308.Clickra`;
- manifest version: `3.11.0.0`;
- manifest Publisher: `CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138`;
- package family: `g1014308.Clickra_mgcm3zc7fc0ty`;
- `Get-AuthenticodeSignature`: `Valid`;
- signer Subject: `CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138`;
- signer Issuer: `Microsoft Marketplace CA G 027`;
- Windows SDK `signtool verify /pa /v`: successfully verified with zero warnings and zero errors.

Negative verification checks also failed closed as intended for a mismatched acquisition hash, mismatched expected version, and mismatched package-family suffix.

Microsoft's Windows endpoint documentation explicitly lists `*.dl.delivery.mp.microsoft.com` and `*.delivery.mp.microsoft.com` as Windows Update / Microsoft Store delivery endpoints that may use HTTP as well as HTTPS. The automation therefore treats transport security as a constrained Microsoft-delivery policy rather than requiring an unsupported HTTPS rewrite of an FE3-issued package URL. Package authenticity remains gated by exact identity/version/package-family checks, independent byte-hash recomputation, and a valid Microsoft Marketplace package signature.

### Phase 5 - Automated Store enrichment

- trigger only after the Store workflow has evidence that the target version is Published, or through a separate condition-watch/reconciliation workflow;
- resolve and download the exact published Store artifact;
- run fail-closed verification;
- attach `Clickra-store.msix` / `Clickra-store.msixbundle` to the already existing same-version GitHub Release;
- update release notes idempotently;
- never replace an existing asset with different bytes automatically.

#### 2026-09-30 reconciliation implementation

The first Phase 5 implementation is intentionally separated from the Store submission job. `scripts/enrich_store_release.py` and `.github/workflows/store-release-enrichment.yml` provide an explicit reconciliation path for an already existing GitHub Release and an already published Store package.

Current behavior:

- accepts an explicit four-part release tag such as `v3.11.0.0`;
- queries the Microsoft Store display catalog and FE3 delivery service using the fixed Clickra Store Product ID;
- selects exactly one matching neutral `.msix` for the requested Clickra identity and version;
- downloads only from the constrained Microsoft delivery domain;
- runs the Phase 4 Store MSIX verifier before any GitHub Release mutation;
- requires acquisition and verifier SHA-256 evidence to match the downloaded bytes;
- uploads the package as the separate Store-channel asset `Clickra-store.msix`;
- does not select `.msixbundle`; bundle enrichment remains disabled until bundle-specific verification is implemented and qualified;
- treats an existing same-name asset with the same GitHub SHA-256 digest as already reconciled;
- fails closed instead of replacing an existing same-name asset when the digest differs or is unavailable;
- appends a marker-delimited Microsoft Store package note exactly once;
- does not alter the Microsoft Store submission workflow or direct-download signing channel.

The workflow currently exposes only `workflow_dispatch` with an explicit tag. This keeps release mutation reviewable while the reconciliation logic is qualified. A recurring condition-watch schedule may be added separately after its cadence and target-release selection policy are explicitly chosen; no polling cadence is assumed by this implementation.

#### 2026-09-30 hosted-runner qualification update

The first two production qualification runs on GitHub-hosted Windows runners failed closed on the original resolver-based transport path: one failure lacked sufficient subprocess diagnostics, and the next exposed HTTP 403 while downloading the resolver-selected Microsoft CDN URL. A temporary self-hosted-runner topology was merged as a proposed workaround, but the maintainer explicitly rejected connecting a personal Windows host to GitHub Actions, so that topology is not part of the final operating model.

Follow-up probes tested Microsoft-supported acquisition paths without changing the GitHub Release. WinGet could identify the public Store listing but required Microsoft Entra ID authentication for package download in the unattended hosted-runner session. Direct resolver probes also remained unreliable. A final GitHub-hosted Windows probe instead used the Microsoft Store display catalog plus FE3 delivery service and succeeded end to end in Actions run `36724724340`: it downloaded `g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.msix` from a Microsoft delivery host and the unchanged verifier confirmed identity `g1014308.Clickra`, version `3.11.0.0`, package family `g1014308.Clickra_mgcm3zc7fc0ty`, valid Microsoft Marketplace signature, SHA-1 `ED11280D94470C24DEC86676C5CF286A60A541DF`, and SHA-256 `23BC746154BFBDE12463B2B3109205EDBBCD8619398BAD0EBF0919C4E607D3CF`.

Production reconciliation therefore returns to `windows-latest`. The Store protocol support is vendored at a pinned MPL-2.0 source revision, the workflow requires only GitHub-hosted Windows x64 plus GitHub CLI/Python/Windows PowerShell, and the full Store identity/signature/hash verification gate remains unchanged before any Release mutation. No self-hosted runner or maintainer workstation connection is required.

---

## 9. Required Test Matrix

Before production rollout, validate at least the following cases.

### Local development

- missing dev certificate -> expected local certificate setup path;
- matching self-signed dev certificate -> package builds and verifies under development policy;
- mismatched Publisher/certificate -> fail closed.

### SignPath direct release

- exact direct Publisher/certificate Subject -> accepted;
- Publisher mismatch -> rejected;
- signing request from disallowed/non-trusted workflow origin -> rejected by SignPath policy;
- unsigned artifact accidentally reaches publication step -> publication blocked;
- signed artifact version differs from tag -> publication blocked.

### Store enrichment

- Store target version not yet available -> no mutation;
- resolver unavailable -> no mutation;
- resolver returns multiple versions -> only exact version considered;
- resolver returns dependencies/resources -> ignored;
- non-Microsoft final artifact host -> rejected;
- wrong Store Identity Name/Publisher -> rejected;
- invalid package signature -> rejected;
- exact verified asset already exists -> idempotent success;
- same filename but different existing GitHub asset bytes -> fail closed/manual review.

---

## 10. Open Questions to Resolve Before Implementation

These are intentionally left undecided until evidence is available:

1. Exact SignPath Foundation certificate Subject/Publisher value assigned to Clickra.
2. Exact SignPath organization/project/signing-policy identifiers and required repository secret names.
3. Exact Store artifact shape for Clickra after publication (`.msix` versus `.msixbundle`, architecture variants, and any related resource packages).
4. Exact Microsoft CDN hostname allowlist observed for Clickra Store package downloads.
5. Whether Store publication detection should extend the existing Store job or live in a separate reconciliation workflow.
6. Whether the Microsoft Store artifact should be exposed primarily as a downloadable archival asset or only as an advanced/diagnostic alternative to Store installation.

No implementation should guess these values.

---

## 11. Acceptance Criteria

The multi-channel design is complete only when all of the following are true:

- local development remains self-signed and easy to test;
- GitHub direct releases use the explicitly selected SignPath Foundation OSS signing path;
- GitHub never publishes an unsigned, development-signed, or unverifiable direct MSIX;
- Store submission remains independent and uses the unchanged Store identity;
- a Store-published package can be resolved, downloaded from Microsoft infrastructure, and independently verified without trusting resolver metadata;
- the verified Store artifact can be added to the same-version GitHub Release idempotently;
- direct and Store artifacts have unambiguous filenames and documentation;
- a failure in any one channel cannot silently substitute an artifact from another channel;
- no named external provider is added or replaced without an explicit provider-selection decision.
