# Visual & Vector Assets

This directory is the **Single Source of Truth** for all visual and vector assets in Clickra.

## Directory Structure

- `features/`: Master SVG vector graphics for all 20 file conversion features (e.g. `word2pdf.svg`, `img-compress.svg`, `translate-pdf.svg`).
- `primary/`: Master SVG (`clickra-icon-primary-master.svg`) and rasterized multi-size PNG/ICO ladders for Clickra application branding.
- `capability/`: Secondary brand identity vector and raster assets used for Microsoft Store logos.

## How to Edit or Add Icons

1. Edit or add the `.svg` files in `features/` or `primary/`.
2. Run the icon generation script to compile runtime ICO files:
   ```bash
   python scripts/generate_feature_icons.py
   ```
3. Run the asset verification script to assert multi-frame ladders:
   ```powershell
   pwsh -NoProfile -File scripts/verify_icon_assets.ps1
   ```

> **Note**: Do NOT edit binary `.ico` or `.png` files in `src/resources/` directly. Always edit the vector sources in this directory.
