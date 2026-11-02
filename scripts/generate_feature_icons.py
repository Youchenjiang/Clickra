"""
Generate modern, pixel-crisp, metaphorically accurate feature icons for Clickra.
- Consistent silhouette and visual hierarchy across all 20 conversion features.
- Explicit target format indicators:
    * Office to PDF clearly indicates PDF Red (#D32F2F) target badge.
    * Markdown to Word clearly indicates Word Blue (#185ABD) target badge.
    * Image format conversions feature bold, high-contrast target format pills (PNG, JPG, WEBP, GIF, HEIC).
- Bold, thick geometry (stroke-width 14-18px) optimized to stay razor-sharp at 16x16 / 24x24 px.
"""

import os
import subprocess
from PIL import Image

def get_svg_definitions():
    return {
        # =========================================================================
        # 1. OFFICE / MARKDOWN TO PDF (Source Card -> Target Red PDF Badge)
        # =========================================================================
        # word2pdf: Word Blue sheet with W -> Red PDF Badge with arrow
        "word2pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base sheet: Word Blue #185ABD -->
  <rect x="24" y="24" width="144" height="152" rx="28" fill="#185ABD"/>
  <!-- Bold W (stroke 16) -->
  <path d="M 52,62 L 68,136 L 96,82 L 124,136 L 140,62" fill="none" stroke="#FFFFFF" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Target badge: PDF Red #D32F2F with diagonal arrow -->
  <rect x="104" y="104" width="128" height="128" rx="28" fill="#D32F2F"/>
  <path d="M 144,192 L 192,144 M 160,144 L 192,144 L 192,176" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # excel2pdf: Excel Green sheet with X -> Red PDF Badge with arrow
        "excel2pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base sheet: Excel Green #107C41 -->
  <rect x="24" y="24" width="144" height="152" rx="28" fill="#107C41"/>
  <!-- Bold X (stroke 18) -->
  <path d="M 60,62 L 132,138 M 132,62 L 60,138" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round"/>
  <!-- Target badge: PDF Red #D32F2F with diagonal arrow -->
  <rect x="104" y="104" width="128" height="128" rx="28" fill="#D32F2F"/>
  <path d="M 144,192 L 192,144 M 160,144 L 192,144 L 192,176" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # ppt2pdf: PPT Orange-Red sheet with P -> Red PDF Badge with arrow
        "ppt2pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base sheet: PPT Orange-Red #D83B01 -->
  <rect x="24" y="24" width="144" height="152" rx="28" fill="#D83B01"/>
  <!-- Bold P (stroke 16) -->
  <path d="M 68,138 L 68,62 L 104,62 C 122,62 128,74 128,88 C 128,102 122,112 104,112 L 68,112" fill="none" stroke="#FFFFFF" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Target badge: PDF Red #D32F2F with diagonal arrow -->
  <rect x="104" y="104" width="128" height="128" rx="28" fill="#D32F2F"/>
  <path d="M 144,192 L 192,144 M 160,144 L 192,144 L 192,176" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # md2pdf: Markdown Purple sheet with M -> Red PDF Badge with arrow
        "md2pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base sheet: Markdown Purple #6048A4 -->
  <rect x="24" y="24" width="144" height="152" rx="28" fill="#6048A4"/>
  <!-- Bold M (stroke 16) -->
  <path d="M 52,136 L 52,64 L 96,104 L 140,64 L 140,136" fill="none" stroke="#FFFFFF" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Target badge: PDF Red #D32F2F with diagonal arrow -->
  <rect x="104" y="104" width="128" height="128" rx="28" fill="#D32F2F"/>
  <path d="M 144,192 L 192,144 M 160,144 L 192,144 L 192,176" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # md2word: Markdown Purple sheet with M -> Word Blue Badge with arrow
        "md2word": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base sheet: Markdown Purple #6048A4 -->
  <rect x="24" y="24" width="144" height="152" rx="28" fill="#6048A4"/>
  <!-- Bold M (stroke 16) -->
  <path d="M 52,136 L 52,64 L 96,104 L 140,64 L 140,136" fill="none" stroke="#FFFFFF" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Target badge: Word Blue #185ABD with diagonal arrow -->
  <rect x="104" y="104" width="128" height="128" rx="28" fill="#185ABD"/>
  <path d="M 144,192 L 192,144 M 160,144 L 192,144 L 192,176" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # =========================================================================
        # 2. PDF OPERATIONS (Unified PDF Red #D32F2F Theme)
        # =========================================================================
        # merge-pdf: Two PDF red sheets combining with bold white plus
        "merge-pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Top-left PDF card -->
  <rect x="24" y="24" width="136" height="136" rx="28" fill="#D32F2F" fill-opacity="0.85"/>
  <path d="M 56,72 L 112,72 M 56,96 L 96,96" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round"/>
  <!-- Bottom-right PDF card -->
  <rect x="96" y="96" width="136" height="136" rx="28" fill="#D32F2F"/>
  <path d="M 128,144 L 184,144 M 128,168 L 168,168" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round"/>
  <!-- Central bold merge plus circle -->
  <circle cx="128" cy="128" r="32" fill="#FFFFFF"/>
  <path d="M 128,112 L 128,144 M 112,128 L 144,128" stroke="#D32F2F" stroke-width="12" stroke-linecap="round"/>
</svg>""",

        # compress-pdf: PDF Red sheet with heavy opposing inward arrows (Vice Squeeze)
        "compress-pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Solid PDF Red background -->
  <rect x="24" y="24" width="208" height="208" rx="36" fill="#D32F2F"/>
  <!-- Central document sheet -->
  <rect x="80" y="72" width="96" height="112" rx="16" fill="#FFFFFF"/>
  <path d="M 98,104 L 138,104 M 98,124 L 146,124 M 98,144 L 128,144" stroke="#D32F2F" stroke-width="10" stroke-linecap="round"/>
  <!-- Left inward arrow (heavy) -->
  <path d="M 38,128 L 66,128 M 52,114 L 66,128 L 52,142" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Right inward arrow (heavy) -->
  <path d="M 218,128 L 190,128 M 204,114 L 190,128 L 204,142" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # split-pdf: Two PDF red sheets parting with outward arrows and center cut
        "split-pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Left card moving left -->
  <rect x="20" y="36" width="98" height="184" rx="24" fill="#D32F2F"/>
  <path d="M 76,128 L 48,128 M 60,116 L 48,128 L 60,140" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Right card moving right -->
  <rect x="138" y="36" width="98" height="184" rx="24" fill="#D32F2F"/>
  <path d="M 180,128 L 208,128 M 196,116 L 208,128 L 196,140" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Center division dotted line -->
  <line x1="128" y1="44" x2="128" y2="212" stroke="#FFFFFF" stroke-width="8" stroke-dasharray="14,12" stroke-linecap="round"/>
</svg>""",

        # translate-pdf: Dual cards with bold A and cycling translation arrows
        "translate-pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base card: Teal Cyan #00838F -->
  <rect x="24" y="24" width="144" height="152" rx="28" fill="#00838F"/>
  <!-- Bold A (stroke 16) -->
  <path d="M 64,136 L 96,62 L 128,136 M 74,112 L 118,112" fill="none" stroke="#FFFFFF" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Target badge: PDF Red #D32F2F with exchange cycle arrows -->
  <rect x="104" y="104" width="128" height="128" rx="28" fill="#D32F2F"/>
  <!-- Top arrow pointing right -->
  <path d="M 136,152 L 192,152 M 178,140 L 192,152 L 178,164" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Bottom arrow pointing left -->
  <path d="M 200,184 L 144,184 M 158,172 L 144,184 L 158,196" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # decrypt-pdf: Gold card with bold unlocked padlock
        "decrypt-pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <rect x="24" y="24" width="208" height="208" rx="36" fill="#D32F2F"/>
  <!-- Bold unlocked shackle -->
  <path d="M 96,104 L 96,72 C 96,52 110,40 128,40 C 146,40 160,52 160,72 L 160,86" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round"/>
  <!-- Padlock body -->
  <rect x="72" y="104" width="112" height="96" rx="22" fill="#FFFFFF"/>
  <circle cx="128" cy="144" r="12" fill="#D32F2F"/>
  <path d="M 128,154 L 128,176" stroke="#D32F2F" stroke-width="10" stroke-linecap="round"/>
</svg>""",

        # print-pdf: Printer Blue card with clean modern printer emitting paper
        "print-pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <rect x="24" y="24" width="208" height="208" rx="36" fill="#4666B0"/>
  <!-- Paper input top -->
  <path d="M 82,56 L 174,56 L 174,96 L 82,96 Z" fill="#FFFFFF"/>
  <!-- Printer body -->
  <rect x="54" y="94" width="148" height="76" rx="16" fill="#FFFFFF"/>
  <circle cx="176" cy="116" r="6" fill="#4666B0"/>
  <!-- Output slot -->
  <rect x="76" y="146" width="104" height="12" rx="4" fill="#4666B0"/>
  <!-- Paper output sheet -->
  <path d="M 82,154 L 174,154 L 174,204 L 82,204 Z" fill="#FFFFFF"/>
  <line x1="98" y1="174" x2="158" y2="174" stroke="#4666B0" stroke-width="8" stroke-linecap="round"/>
  <line x1="98" y1="188" x2="142" y2="188" stroke="#4666B0" stroke-width="8" stroke-linecap="round"/>
</svg>""",

        # =========================================================================
        # 3. IMAGE SUITE (Image / Photo Base + Operations & Red PDF Badge)
        # =========================================================================
        # img2pdf: Photo card -> PDF Red Badge with arrow
        "img2pdf": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base sheet: Image Goldenrod #E65100 -->
  <rect x="24" y="24" width="144" height="152" rx="28" fill="#E65100"/>
  <!-- Photo landscape (sun + twin mountains) -->
  <circle cx="64" cy="64" r="14" fill="#FFFFFF"/>
  <path d="M 46,136 L 76,96 L 100,120 L 122,88 L 152,136" fill="none" stroke="#FFFFFF" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Target badge: PDF Red #D32F2F with diagonal arrow -->
  <rect x="104" y="104" width="128" height="128" rx="28" fill="#D32F2F"/>
  <path d="M 144,192 L 192,144 M 160,144 L 192,144 L 192,176" fill="none" stroke="#FFFFFF" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # img-merge: Two photo cards combining with merge plus
        "img-merge": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Top-left photo card -->
  <rect x="24" y="24" width="136" height="136" rx="28" fill="#8E44AD" fill-opacity="0.85"/>
  <circle cx="58" cy="58" r="12" fill="#FFFFFF"/>
  <path d="M 44,116 L 70,82 L 92,104 L 112,78 L 138,116" fill="none" stroke="#FFFFFF" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Bottom-right photo card -->
  <rect x="96" y="96" width="136" height="136" rx="28" fill="#8E44AD"/>
  <circle cx="130" cy="130" r="12" fill="#FFFFFF"/>
  <path d="M 116,188 L 142,154 L 164,176 L 184,150 L 210,188" fill="none" stroke="#FFFFFF" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Central merge plus circle -->
  <circle cx="128" cy="128" r="32" fill="#FFFFFF"/>
  <path d="M 128,112 L 128,144 M 112,128 L 144,128" stroke="#8E44AD" stroke-width="12" stroke-linecap="round"/>
</svg>""",

        # img-stitch: Two photo cards stitching together vertically
        "img-stitch": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Top photo strip -->
  <rect x="36" y="24" width="184" height="96" rx="22" fill="#5C4FB5"/>
  <circle cx="72" cy="54" r="10" fill="#FFFFFF"/>
  <path d="M 60,94 L 88,64 L 112,86 L 136,58 L 166,94" fill="none" stroke="#FFFFFF" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Bottom photo strip -->
  <rect x="36" y="136" width="184" height="96" rx="22" fill="#5C4FB5"/>
  <circle cx="72" cy="166" r="10" fill="#FFFFFF"/>
  <path d="M 60,206 L 88,176 L 112,198 L 136,170 L 166,206" fill="none" stroke="#FFFFFF" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Center stitch link pin -->
  <rect x="116" y="104" width="24" height="48" rx="8" fill="#FFFFFF"/>
</svg>""",

        # img-compress: Image frame with inward vice arrows
        "img-compress": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <rect x="24" y="24" width="208" height="208" rx="36" fill="#7A5C00"/>
  <!-- Central photo frame -->
  <rect x="76" y="76" width="104" height="104" rx="20" fill="#FFFFFF"/>
  <circle cx="102" cy="102" r="10" fill="#7A5C00"/>
  <path d="M 90,154 L 110,126 L 128,144 L 144,122 L 166,154" fill="none" stroke="#7A5C00" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Left inward arrow (heavy) -->
  <path d="M 38,128 L 64,128 M 50,114 L 64,128 L 50,142" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- Right inward arrow (heavy) -->
  <path d="M 218,128 L 192,128 M 206,114 L 192,128 L 206,142" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
</svg>""",

        # =========================================================================
        # 4. IMAGE FORMAT CONVERSIONS (Photo Base + Crisp Format Pill Badge)
        # =========================================================================
        # img-to-png: Photo Base + Vivid Blue PNG Pill
        "img-to-png": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base photo card -->
  <rect x="24" y="24" width="168" height="144" rx="28" fill="#ECEFF1"/>
  <circle cx="68" cy="68" r="14" fill="#0078D4"/>
  <path d="M 48,136 L 80,94 L 106,120 L 130,86 L 164,136" fill="none" stroke="#0078D4" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- PNG Format Pill Badge: Blue #0078D4 -->
  <rect x="64" y="124" width="168" height="108" rx="26" fill="#0078D4"/>
  <!-- Bold PNG Lettering -->
  <text x="148" y="196" font-family="Segoe UI, Arial, sans-serif" font-size="52" font-weight="900" fill="#FFFFFF" text-anchor="middle" letter-spacing="2">PNG</text>
</svg>""",

        # img-to-jpg: Photo Base + Warm Amber JPG Pill
        "img-to-jpg": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base photo card -->
  <rect x="24" y="24" width="168" height="144" rx="28" fill="#FFF3E0"/>
  <circle cx="68" cy="68" r="14" fill="#E65100"/>
  <path d="M 48,136 L 80,94 L 106,120 L 130,86 L 164,136" fill="none" stroke="#E65100" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- JPG Format Pill Badge: Amber #E65100 -->
  <rect x="64" y="124" width="168" height="108" rx="26" fill="#E65100"/>
  <text x="148" y="196" font-family="Segoe UI, Arial, sans-serif" font-size="52" font-weight="900" fill="#FFFFFF" text-anchor="middle" letter-spacing="2">JPG</text>
</svg>""",

        # img-to-webp: Photo Base + Cyan WEBP Pill
        "img-to-webp": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base photo card -->
  <rect x="24" y="24" width="168" height="144" rx="28" fill="#E0F7FA"/>
  <circle cx="68" cy="68" r="14" fill="#00838F"/>
  <path d="M 48,136 L 80,94 L 106,120 L 130,86 L 164,136" fill="none" stroke="#00838F" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- WEBP Format Pill Badge: Cyan #00838F -->
  <rect x="56" y="124" width="176" height="108" rx="26" fill="#00838F"/>
  <text x="144" y="194" font-family="Segoe UI, Arial, sans-serif" font-size="44" font-weight="900" fill="#FFFFFF" text-anchor="middle" letter-spacing="1">WEBP</text>
</svg>""",

        # img-to-gif: Photo Base + Purple-Magenta GIF Pill
        "img-to-gif": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base photo card -->
  <rect x="24" y="24" width="168" height="144" rx="28" fill="#F3E5F5"/>
  <circle cx="68" cy="68" r="14" fill="#8E24AA"/>
  <path d="M 48,136 L 80,94 L 106,120 L 130,86 L 164,136" fill="none" stroke="#8E24AA" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- GIF Format Pill Badge: Purple #8E24AA -->
  <rect x="64" y="124" width="168" height="108" rx="26" fill="#8E24AA"/>
  <text x="148" y="196" font-family="Segoe UI, Arial, sans-serif" font-size="52" font-weight="900" fill="#FFFFFF" text-anchor="middle" letter-spacing="2">GIF</text>
</svg>""",

        # img-to-heic: Photo Base + Apple Green HEIC Pill
        "img-to-heic": """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- Base photo card -->
  <rect x="24" y="24" width="168" height="144" rx="28" fill="#E8F5E9"/>
  <circle cx="68" cy="68" r="14" fill="#2E7D32"/>
  <path d="M 48,136 L 80,94 L 106,120 L 130,86 L 164,136" fill="none" stroke="#2E7D32" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>
  <!-- HEIC Format Pill Badge: Green #2E7D32 -->
  <rect x="56" y="124" width="176" height="108" rx="26" fill="#2E7D32"/>
  <text x="144" y="194" font-family="Segoe UI, Arial, sans-serif" font-size="44" font-weight="900" fill="#FFFFFF" text-anchor="middle" letter-spacing="1">HEIC</text>
</svg>"""
    }

def generate_icons(repo_root=None):
    if repo_root is None:
        repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
        
    svg_dir = os.path.join(repo_root, "packaging", "brand_assets", "features")
    res_dir = os.path.join(repo_root, "src", "resources")
    os.makedirs(svg_dir, exist_ok=True)
    os.makedirs(res_dir, exist_ok=True)
    
    edge_candidates = [
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    ]
    edge_path = next((p for p in edge_candidates if os.path.exists(p)), None)
    if not edge_path:
        raise RuntimeError("Microsoft Edge executable not found for headless SVG rasterization.")

    definitions = get_svg_definitions()
    sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64)]
    
    temp_dir = os.path.join(repo_root, "packaging", "brand_assets", "features", ".temp")
    os.makedirs(temp_dir, exist_ok=True)
    
    print(f"Generating {len(definitions)} improved feature icons...")
    for cmd, svg_content in definitions.items():
        svg_file = os.path.join(svg_dir, f"{cmd}.svg")
        png_temp = os.path.join(temp_dir, f"{cmd}.png")
        ico_file = os.path.join(res_dir, f"menu-{cmd}.ico")
        
        # 1. Write clean standalone SVG
        with open(svg_file, "w", encoding="utf-8") as f:
            f.write(svg_content.strip() + "\n")
            
        # 2. Rasterize to 256x256 antialiased PNG via Edge Headless
        cmd_args = [
            edge_path,
            "--headless=new",
            "--disable-gpu",
            f"--screenshot={png_temp}",
            "--window-size=256,256",
            "--default-background-color=00000000",
            svg_file
        ]
        subprocess.run(cmd_args, capture_output=True, check=True)
        
        # 3. Assemble multi-resolution ICO (16, 20, 24, 32, 48, 64)
        with Image.open(png_temp) as img:
            img.save(ico_file, format="ICO", sizes=sizes)
            
        print(f"  [OK] {cmd}.svg -> menu-{cmd}.ico")
        
    # Clean up temp pngs
    for f in os.listdir(temp_dir):
        try: os.remove(os.path.join(temp_dir, f))
        except: pass
    try: os.rmdir(temp_dir)
    except: pass
    
    print("All 20 improved feature icons generated successfully.")

if __name__ == "__main__":
    generate_icons()
