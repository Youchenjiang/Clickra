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

### 3.1 Resolver

The selected resolver for this workflow is `store.rg-adguard.net`.

Its responsibility is limited to discovering Microsoft Store package download links for the Clickra Store product. It is not a trust authority and its response must never be treated as sufficient proof that an artifact is safe or belongs to Clickra.

The authoritative artifact bytes must be downloaded from the Microsoft-hosted CDN URL returned by the resolver.

### 3.2 Trust boundary

The resolver answers the question:

> "Where can this Store package be downloaded?"

It does **not** answer:

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
3. Once published, query `store.rg-adguard.net` using the Clickra Store product identity/URL and the intended Retail channel.
4. Parse candidate package entries.
5. Select candidates only by exact identity/version/package-type rules.
6. Download the candidate from the Microsoft-hosted CDN.
7. Run the complete Store artifact verification gate described above.
8. Only after verification, attach the Store-distributed artifact to the existing GitHub Release for the same version.
9. Update release notes to indicate that the Microsoft Store version is available and identify the Store artifact separately from the direct artifact.

### Phase C - Failure/retry behavior

The Store enrichment workflow must be idempotent.

- If the Store version is not published yet: make no GitHub Release changes.
- If the resolver is unavailable: make no GitHub Release changes.
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

2. **store.rg-adguard.net**
   - role: resolver for Microsoft Store/CDN package locations after Store publication;
   - scope: discovery only;
   - never serves as the trust anchor for publication.

3. **Microsoft Store / Microsoft CDN**
   - role: Store submission, certification, Store distribution, and authoritative artifact hosting for the Store-distributed package.

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

#### 2026-09-30 prototype observation

The read-only prototype in `scripts/resolve_store_package.py` successfully identified the exact published Clickra Store candidate for version `3.11.0.0` without downloading package bytes:

- package: `g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.msix`;
- package type: single `.msix` rather than a bundle;
- architecture marker: `neutral`;
- package CDN host observed: `tlu.dl.delivery.mp.microsoft.com`;
- BlockMap CDN host observed in the same resolver response: `dl.delivery.mp.microsoft.com`.

The resolver currently returned these candidate URLs with the `http://` scheme. The prototype intentionally treats that as a fail-closed transport mismatch even when every identity/version/type/architecture check matches, so the candidate is reported for review but not selected for download. No GitHub Release mutation and no Store package download were performed during this observation.

Phase 4 must therefore establish an acceptable HTTPS retrieval path and independently verify the final downloaded URL, package identity, version, package family, and signature before Store enrichment can advance.

### Phase 4 - Store package verification

- download a known already-published Clickra Store version;
- inspect manifest identity/version/package family;
- inspect package/signature state;
- establish the exact verification assertions required for automation;
- verify behavior for bundles, resource packages, dependencies, and multiple architectures if returned.

### Phase 5 - Automated Store enrichment

- trigger only after the Store workflow has evidence that the target version is Published, or through a separate condition-watch/reconciliation workflow;
- resolve and download the exact published Store artifact;
- run fail-closed verification;
- attach `Clickra-store.msix` / `Clickra-store.msixbundle` to the already existing same-version GitHub Release;
- update release notes idempotently;
- never replace an existing asset with different bytes automatically.

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
