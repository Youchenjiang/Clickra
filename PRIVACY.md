# Privacy Policy for Clickra

**Last Updated:** September 12, 2026

## Overview
This Privacy Policy describes how Clickra ("the App") handles your data.

## Data Collection and Usage
**Clickra does not collect, store, or transmit any personal information, telemetry, analytics data, or usage logs.**

The App is architected as a **Local-First utility**. All core file processing operations (PDF merging, splitting, compression, decryption, Office-to-PDF conversion, image conversion, compression, and stitching) are performed **100% locally on your device** and do not require an internet connection. Your files and documents are never uploaded to our servers.

### Explicit Network Operations

1. **PDF Translation (`translate-pdf`)**:
   If you choose to use the optional **PDF Translation** feature, text extracted from the document is sent securely over HTTPS to the translation provider API (such as Google Translate: `translate.googleapis.com` or MyMemory: `api.mymemory.translated.net`) solely to produce the translated text. The original PDF files are not uploaded, and Clickra does not retain or store any translated text.

2. **On-Demand LibreOffice Download (Settings)**:
   If Microsoft Office is not installed and you choose to install the optional LibreOffice engine via the Settings page, Clickra downloads the official MSI installer package directly from The Document Foundation (`https://download.documentfoundation.org/`). No personal or device identifiers are sent with this download request.

## Third-Party Services
Except for the user-initiated translation API requests and the optional LibreOffice installer download, Clickra does not integrate with any third-party cloud services, tracking SDKs, advertising networks, or analytics platforms.

## Contact Us
If you have any questions or concerns about this Privacy Policy, please contact the developer via the GitHub repository: https://github.com/YouchenJiang/Clickra
