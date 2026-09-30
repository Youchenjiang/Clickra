# SignPath Foundation Application Readiness

## 1. Purpose

This document is the application evidence pack and readiness checklist for Clickra's planned use of SignPath Foundation Open Source Code Signing for GitHub direct-download releases.

It does not authorize or perform an external application submission. Unknown SignPath-assigned identifiers, certificate subjects, secrets, and organization settings must remain unset until they are provided through the accepted SignPath onboarding process.

---

## 2. Project facts

| Field | Clickra evidence |
| --- | --- |
| Project | Clickra |
| Repository | `https://github.com/Youchenjiang/Clickra` |
| Repository visibility | Public |
| Repository owner | `Youchenjiang` |
| License | Apache License 2.0 |
| Primary platform | Windows 10 / 11 |
| Application type | Windows context-menu utility and file-processing suite |
| Existing releases | Yes; public GitHub releases exist |
| Microsoft Store listing | Product ID `9NGLBF6P1KLD` |
| Privacy policy | `PRIVACY.md` |
| Code signing policy | `docs/CODE_SIGNING_POLICY.md` |
| Planned trusted build | GitHub Actions on GitHub-hosted Windows runners |
| Planned signed artifact | GitHub direct-download MSIX |

---

## 3. Eligibility mapping

### 3.1 Open-source license

**Status: evidence available.**

The repository declares Apache License 2.0. Before submission, re-check that all Clickra-maintained components included in the signed artifact remain under compatible open-source terms and that no proprietary maintainer-owned component has been added.

### 3.2 Existing released software

**Status: evidence available.**

Clickra already publishes versioned releases and is distributed through the Microsoft Store. The SignPath target is the same Clickra application in its GitHub direct-download MSIX distribution form.

### 3.3 Active maintenance

**Status: evidence available.**

The repository is active and receives current source, packaging, CI, and release updates. Capture the current repository activity/release URLs at application time rather than embedding a stale timestamp in this policy.

### 3.4 Documented functionality

**Status: evidence available.**

`README.md` documents Clickra's purpose, supported Windows versions, major conversion/file-processing features, privacy behavior, installation paths, local build, and testing.

### 3.5 Malware / unwanted-software restriction

**Status: application assertion requires maintainer confirmation.**

Clickra is intended as a productivity/file-processing utility, not as malware, potentially unwanted software, an exploitation framework, or a security-circumvention tool. Before submitting the application, the maintainer must confirm that no released feature falls within SignPath Foundation's excluded hacking-tool or unwanted-software categories.

### 3.6 Proprietary-code restriction

**Status: direct-package managed dependency inventory reviewed; re-check at submission.**

The repository contains open-source Clickra code and documents redistributed third-party dependencies in `THIRD-PARTY-NOTICES.txt`. A local resolved-package inventory of the NativeAOT CLI/Core path currently shows the following package license metadata:

| Package | Resolved version | License metadata |
| --- | ---: | --- |
| PDFsharp / PdfSharp | 6.2.4 | MIT |
| PdfPig | 0.1.14 | Apache-2.0 |
| Imazen.WebP | 11.0.0 | MIT |
| Imazen.WebP.NativeRuntime.win-x64 | 1.6.1 | MIT |
| System.Drawing.Common | 8.0.31 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT |
| Microsoft.Win32.SystemEvents | 8.0.0 | MIT |
| System.Security.Cryptography.Pkcs | 8.0.1 | MIT |
| Microsoft.DotNet.ILCompiler | 8.0.31 | MIT |
| Microsoft.NET.ILLink.Tasks | 8.0.31 | MIT |

`THIRD-PARTY-NOTICES.txt` also records libwebp under the BSD 3-Clause license. The Shell and Launcher projects do not currently declare additional NuGet package references.

Before application submission, repeat this inventory against the exact release source and classify every binary actually present in the direct MSIX as:

- Clickra-maintained open-source code;
- upstream open-source dependency;
- permitted system library/runtime component;
- excluded/proprietary component.

The current resolved managed dependency inventory shows no proprietary package license. This is evidence for readiness, not a permanent exemption from re-checking the exact release artifact.

### 3.7 Privacy and network behavior

**Status: evidence available.**

`PRIVACY.md` states that Clickra has no telemetry or analytics and that core processing is local. It documents user-initiated network behavior for optional PDF translation and optional LibreOffice download.

### 3.8 Uninstallation and system changes

**Status: public documentation added; verify final wording before submission.**

The application is distributed as MSIX/Store software and Windows provides package removal facilities. `README.md` documents uninstall through **Settings > Apps > Installed apps** and states that the packaged Explorer integration is removed with the app package. Optional external software is explicitly described as independently managed.

---

## 4. Required code-signing roles

Current repository collaborator evidence shows one administrator: `Youchenjiang`.

Planned roles:

- **Committer / author:** `Youchenjiang`
- **Reviewer:** `Youchenjiang` for changes proposed by non-committers
- **Signing approver:** `Youchenjiang`

Every production SignPath signing request requires manual approval. If SignPath or repository membership changes, the public Code signing policy must be updated before the new member participates in signing.

### MFA gate

Before submission/activation, verify rather than assume:

- [ ] GitHub MFA enabled for every signing-role account.
- [ ] SignPath MFA enabled for every signing-role account.

---

## 5. Trusted-build design

The planned SignPath integration must use GitHub as a trusted build system with origin verification.

Required design properties:

- build occurs on GitHub-hosted runners;
- the unsigned direct artifact is first stored as a GitHub workflow artifact;
- the signing request is submitted from that workflow artifact;
- repository URL in SignPath exactly identifies the Clickra repository;
- origin verification restricts production signing to the approved release source policy;
- source review includes workflow files, packaging scripts, manifests, and any build-time external inputs;
- the release signing request is manually approved;
- the signed MSIX is independently verified by Clickra before GitHub Release publication.

Do not create the provider-specific production workflow until the accepted SignPath project exposes the exact organization/project/artifact/signing-policy configuration needed by the integration.

---

## 6. Artifact configuration readiness

SignPath Foundation requires signed binary metadata to identify the project and use consistent version metadata.

Before integration, verify the exact direct package/binary metadata that SignPath will enforce:

- [ ] product name resolves consistently to `Clickra` for signed Clickra binaries;
- [ ] product/file/package versions correspond to the same release version;
- [ ] the direct MSIX Publisher is the exact certificate Subject assigned to the accepted signing configuration;
- [ ] upstream binaries are not accidentally submitted as independent Clickra signing targets;
- [ ] the artifact configuration signs only the files intended to receive the Clickra release signature.

The exact SignPath artifact configuration must be derived from the real accepted project and artifact layout, not guessed in advance.

---

## 7. Public repository changes required before application

- [x] Add a dedicated `Code signing policy` document.
- [x] Link the Code signing policy from the project home page (`README.md`).
- [ ] Ensure the release/download presentation links or refers to the Code signing policy when SignPath-signed downloads are introduced.
- [x] Add public installation/uninstallation documentation.
- [x] Complete the current managed dependency/license inventory for the direct NativeAOT path; repeat against the exact release artifact before submission.
- [ ] Verify GitHub MFA for all signing-role accounts.
- [ ] Review the final public policy wording against the current SignPath Foundation conditions immediately before submission.

---

## 8. Application-ready project description

Use the following as the working description for the SignPath Foundation application. Adjust only to match the actual application form fields at submission time.

> Clickra is an Apache-2.0 licensed Windows 10/11 context-menu utility suite for local document, PDF, and image workflows. It provides NativeAOT Explorer integration and local file-processing tools including Office-to-PDF conversion, PDF merge/split/compression/decryption, image conversion and stitching, with optional user-initiated PDF translation and LibreOffice download features documented in its privacy policy. Clickra is publicly developed and released from https://github.com/Youchenjiang/Clickra and is also distributed through the Microsoft Store. We are requesting SignPath Foundation Open Source Code Signing for the GitHub direct-download MSIX. The intended release path uses GitHub Actions on GitHub-hosted runners, SignPath trusted-build/origin verification, manual signing approval, and post-sign identity/version/signature verification before publishing the artifact to GitHub Releases.

---

## 9. Evidence links to provide at submission

Prepare current URLs for:

- GitHub repository home page;
- Apache-2.0 `LICENSE`;
- latest GitHub Release;
- Microsoft Store product page;
- `README.md` feature/download documentation;
- `PRIVACY.md`;
- public Code signing policy;
- relevant GitHub Actions release workflow after SignPath integration is ready for review, if requested by SignPath.

Do not provide unpublished secrets, private keys, API tokens, or internal credentials in the application.

---

## 10. Values that must remain unresolved until onboarding

Do not invent or preconfigure any of the following:

- SignPath organization ID;
- SignPath project slug/ID;
- artifact configuration slug/ID;
- signing policy slug/ID;
- API token secret name/value beyond what the accepted integration explicitly requires;
- certificate Subject / direct MSIX Publisher;
- certificate identifiers;
- approval policy identifiers;
- webhook credentials or callback configuration.

These values become implementation inputs only after SignPath accepts/configures the project.

---

## 11. Submission gate

The SignPath Foundation application is ready to submit only when:

- the public Code signing policy is linked from `README.md`;
- the dependency/proprietary-code inventory is clean;
- installation/uninstallation disclosure is sufficient;
- GitHub MFA has been verified for the signing-role account(s);
- project/release/privacy links have been reviewed for accuracy;
- the application description matches the current Clickra behavior;
- the maintainer explicitly authorizes the external application submission.
