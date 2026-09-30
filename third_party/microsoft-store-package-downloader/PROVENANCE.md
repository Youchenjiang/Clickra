# Microsoft Store package downloader provenance

The files in this directory are vendored for Clickra's Microsoft Store
post-publication reconciliation workflow.

- Upstream: `https://github.com/hanyu1212/microsoft-store-package-downloader-skill`
- Pinned upstream commit: `d1c508cc458223ec7ae684e685625c5b7cb62286`
- Upstream license: Mozilla Public License 2.0 (`LICENSE`)
- StoreLib-derived source: `references/storelib/FE3Handler.cs` and the FE3 XML templates
- StoreLib license: Mozilla Public License 2.0 (`references/storelib/LICENSE`)

Clickra executes the vendored helper only from the manually dispatched Store
reconciliation workflow. The helper resolves package fulfillment through the
Microsoft Store display catalog and Microsoft FE3 delivery service; Clickra's
own verifier remains the publication trust gate.
