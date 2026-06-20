# PDF Diagnostics Tools

Reusable local diagnostics for PDF translation layout bugs.

Run from this directory:

```powershell
cd tools\pdf-diagnostics
python render_pages.py pentest 5 7 8
```

The tools read fixtures from local `test_pdfs/source` and
`test_pdfs/translated`. Generated PNGs, dumps, and reports are written under
ignored `scratch/output`.

## PDF Rendering
| Script | Purpose | Example |
|--------|---------|---------|
| `render_pages.py` | Render PDF pages to PNG | `python render_pages.py pentest 5 7 8` |
| `render_compare.py` | Side-by-side orig vs trans | `python render_compare.py --orig pentest 5 7 8` |
| `render_links.py` | Render link rects on page | `python render_links.py 2407 1 9 13` |

## Text Inspection
| Script | Purpose | Example |
|--------|---------|---------|
| `inspect_blocks.py` | Dump page blocks/spans/drawings/links | `python inspect_blocks.py sem 1 -m rawdict` |
| `analyze_page.py` | Column breakdown (CJK/ENG/simp) | `python analyze_page.py 2407 10` |

## Character Analysis
| Script | Purpose | Example |
|--------|---------|---------|
| `find_tofu.py` | Find tofu, CJK-font-Latin chars | `python find_tofu.py final` |
| `scan_simp.py` | Count simplified Chinese chars | `python scan_simp.py 2407` |

## Link & Citation
| Script | Purpose | Example |
|--------|---------|---------|
| `validate_links.py` | Validate link alignment src vs trans | `python validate_links.py --src 2407` |

## Chart & Table
| Script | Purpose | Example |
|--------|---------|---------|
| `chart_analysis.py` | Chart region text analysis/comparison | `python chart_analysis.py pentest 4 7 --compare pentest_translated` |

## Image Operations
| Script | Purpose | Example |
|--------|---------|---------|
| `crop_region.py` | Crop rectangle from PDF page | `python crop_region.py sem 1 0 0 300 400` |

## API Testing
| Script | Purpose | Example |
|--------|---------|---------|
| `test_translate_api.py` | Test Google Translate endpoints | `python test_translate_api.py` |
| `test_translate_large.py` | Test mobile translate with large text | `python test_translate_large.py` |

## Diagnostic
| Script | Purpose | Example |
|--------|---------|---------|
| `pdf_health.py` | Full-doc health report (CJK/simp/tofu/per-page) | `python pdf_health.py pentest` |
| `find_untranslated.py` | Find right-column English that wasn't translated | `python find_untranslated.py pentest` |
| `font_analysis.py` | Font usage per page, detect coverage issues | `python font_analysis.py pentest` |
| `visual_diff.py` | Pixel-diff overlay between orig and trans | `python visual_diff.py --orig pentest 5 7` |

## Verification (project-specific)
| Script | Purpose | Example |
|--------|---------|---------|
| `verify_togll_fix.py` | TOGLL regression check | `python verify_togll_fix.py` |
| `verify_p14_wd.py` | Final project page 14 table check | `python verify_p14_wd.py` |
| `analyze_final_tables.py` | Final project tables p14-16 | `python analyze_final_tables.py` |
| `compare_final_tables.py` | Table structure comparison | `python compare_final_tables.py` |

## Utilities
| Script | Purpose | Example |
|--------|---------|---------|
| `audit_pentest_pixels.py` | Pixel-level gray/white audit | `python audit_pentest_pixels.py` |
| `extract_page.py` | Extract pages to separate PDF | `python extract_page.py sem 1 3 5` |

## Source Keys
Use these shortcuts instead of full paths: `sem`, `2407`, `final`, `pentest`, `togll`
