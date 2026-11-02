# Visual & Vector Assets

This directory is the **Single Source of Truth** for all visual and vector assets in Clickra.

## Directory Structure

- `features/`: Master SVG vector graphics for all 20 file conversion features (e.g. `word2pdf.svg`, `img-compress.svg`, `translate-pdf.svg`).
- `primary/`: Master SVG (`clickra-icon-primary-master.svg`) and rasterized multi-size PNG/ICO ladders for Clickra application branding.
- `capability/`: Secondary brand identity vector and raster assets used for Microsoft Store logos.
- `icons/`: Build-time generated `.ico` and `app.png` multi-frame assets (compiled by `.NET IconGen`).

## How to Edit or Add Icons

1. Edit or add the `.svg` files in `features/` or `primary/`.
2. Build the project or run the pure .NET icon compiler:
   ```bash
   dotnet run --project scripts/tools/IconGen
   ```
   (Builds will also incrementally recompile icons automatically if any SVG is modified).
3. Run the asset verification script to assert multi-frame ladders:
   ```powershell
   pwsh -NoProfile -File scripts/verify_icon_assets.ps1
   ```
