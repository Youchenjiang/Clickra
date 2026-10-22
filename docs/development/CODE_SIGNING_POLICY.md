# Code signing policy

Clickra uses separate signing and trust models for local development, GitHub direct downloads, and Microsoft Store distribution. This policy covers the GitHub direct-download production signing path.

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Project and repository

- Project: Clickra
- Source repository: <https://github.com/Youchenjiang/Clickra>
- License: Apache License 2.0
- Production direct-download artifacts must be built from the public Clickra repository through the approved GitHub Actions trusted-build workflow.

## Team roles

Clickra is currently maintained as a single-maintainer project.

- **Committer / author:** [Youchenjiang](https://github.com/Youchenjiang), repository owner and administrator.
- **Reviewer:** [Youchenjiang](https://github.com/Youchenjiang). Changes proposed by external contributors must be reviewed before they are accepted into a release source branch.
- **Code-signing approver:** [Youchenjiang](https://github.com/Youchenjiang). Every production signing request must receive the manual approval required by the SignPath Foundation Open Source Code Signing policy.

If additional maintainers receive repository or SignPath permissions, this section must be updated before they participate in production code signing.

## Account security

All people assigned a Clickra code-signing role must use multi-factor authentication for both GitHub and SignPath. Production SignPath signing must remain disabled until this requirement has been verified for every signing-role account.

## What may be signed

The SignPath Foundation signing configuration may be used only for Clickra release artifacts that:

- are built from source and build scripts maintained in the Clickra repository;
- originate from the repository and branch/tag policy configured for the SignPath project;
- are produced by the approved GitHub-hosted trusted-build workflow;
- pass SignPath origin verification;
- match the release version being published;
- contain Clickra binaries and permitted upstream/system dependencies only;
- pass Clickra's post-sign package identity and signature verification before publication.

Development builds, locally uploaded binaries, artifacts from unverified workflow origins, unrelated projects, and manually substituted binaries must not be signed with the Clickra SignPath Foundation signing configuration.

## Release signing process

For a production GitHub direct release:

1. An explicitly authorized Clickra release tag starts the GitHub-hosted release workflow.
2. The workflow builds the unsigned direct-distribution MSIX from the tagged Clickra source.
3. The unsigned artifact is uploaded to GitHub Actions before the signing request is submitted.
4. SignPath verifies trusted-build origin metadata and the configured source/build policy.
5. The designated Clickra approver manually approves the production signing request.
6. The signed artifact is returned to the release workflow.
7. Clickra independently verifies package identity, exact release version, signer/Publisher consistency, and production signature state.
8. Only the verified artifact may be attached to the GitHub Release.

Signing failure, denied approval, origin-verification failure, identity mismatch, version mismatch, or signature-verification failure must stop direct artifact publication.

## Build and review scope

Source review for production releases includes build and signing infrastructure. Changes to GitHub Actions workflows, packaging scripts, manifests, signing verification, or SignPath integration are security-sensitive changes and must receive the same review attention as application source changes.

The tracked Microsoft Store Publisher identity must not be replaced by the GitHub direct-distribution Publisher. The direct package is rebuilt with its production signing Publisher only inside the release build and the tracked Store identity files are restored unchanged.

## Privacy policy

Clickra's privacy policy is published at [PRIVACY.md](../PRIVACY.md).

Clickra collects no telemetry or analytics. Core file processing is local. Network access occurs only for user-initiated optional functionality documented in the privacy policy, including PDF translation providers and optional LibreOffice installer download.

## Incident response and revocation

If a signing credential, SignPath account, repository account, trusted-build configuration, or published signed artifact is suspected to be compromised:

- stop new production signing requests;
- preserve the relevant repository, workflow, signing-request, and release evidence;
- investigate the affected source revision and build provenance;
- remove or quarantine affected release assets when justified and explicitly authorized;
- notify SignPath Foundation when certificate misuse or a policy violation may have occurred;
- resume signing only after the trust boundary has been restored and revalidated.

## Other distribution channels

- **Local development/testing:** uses Clickra self-signed development certificates and is not production trust.
- **Microsoft Store:** uses the Store package identity and Microsoft Store certification/distribution signing independently of the SignPath direct-release path.

The complete multi-channel design is documented in [Multi-Channel Signing and Distribution Plan](development/multi_channel_signing_distribution_plan.md).
