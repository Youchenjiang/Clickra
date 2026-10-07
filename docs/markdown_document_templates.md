# Markdown document templates

Clickra uses the same renderer-neutral document-template model for Markdown → PDF and Markdown → Word.

For ordinary users, the supported import workflow is **a Word `.docx` template**. Clickra reads the document's Word styles and page layout, converts the relevant settings into its internal template model, and applies that model consistently to both PDF and DOCX output. Users do not need to author a Clickra-specific JSON file.

The JSON format documented later in this file remains an internal/advanced interchange format for compatibility and automated workflows. It is not exposed as the normal template picker in the product UI.

## Importing a Word template

1. Start Markdown → PDF or Markdown → Word.
2. Open **More options**.
3. Choose **Import DOCX...** and select the Word template supplied by a school, organization, or team.
4. Confirm the selected filename and start the conversion.

The importer currently extracts these semantic settings from `word/styles.xml` and the final section properties in `word/document.xml`:

- Normal-style Latin and East Asian font families.
- Normal-style body size and line spacing.
- Normal-style paragraph-after spacing, first-line indent, and full justification.
- Heading 1–6 sizes, including heading styles identified through Word outline levels.
- Heading 1 centered alignment.
- Independent top, right, bottom, and left page margins.
- `basedOn` style inheritance and Word document defaults where applicable.

Clickra does not copy arbitrary Word XML into the output. Unsupported decorative Word features remain governed by Clickra's renderer-neutral template defaults so PDF and DOCX stay semantically aligned. Paper size, text-size scaling, and code-block theme remain explicit conversion choices.

The selected DOCX applies only to that conversion. It does not modify the original Word file or install a persistent template into Settings.

## Advanced JSON interchange format

The data-only JSON format is retained for backward compatibility, tests, automation, and advanced tooling. It does not execute code or allow arbitrary template scripts.

### Quick example

```json
{
  "version": 1,
  "name": "Course Handout",
  "base": "academic",
  "typography": {
    "latinFont": "Segoe UI",
    "cjkFont": "Microsoft JhengHei",
    "monospaceFont": "Courier New",
    "bodySize": 12,
    "lineHeight": 18,
    "headings": {
      "h1": 28,
      "h2": 19
    }
  },
  "layout": {
    "margin": 48,
    "marginLeft": 60,
    "blockGap": 9,
    "firstLineIndent": 0,
    "centerH1": false,
    "justifyBody": false,
    "accentH2": true,
    "drawH2Bar": true,
    "fillTableHeader": true,
    "quoteBarWidth": 3
  },
  "palette": {
    "body": "#334155",
    "strong": "#1E293B",
    "accent": "#0F766E",
    "softAccent": "#CCFBF1",
    "border": "#CBD5E1"
  }
}
```

Only properties present in the JSON override the selected base template. Omitted properties inherit from the base.

### Root properties

| Property | Required | Meaning |
| --- | --- | --- |
| `version` | Yes | Template format version. The current and only supported value is `1`. |
| `name` | No | Display identity for the imported template. Defaults to the JSON filename without its extension. |
| `base` | No | Built-in template to inherit from: `default`, `minimal`, or `academic`. Defaults to `default`. |
| `typography` | No | Font families, body metrics, and heading sizes. |
| `layout` | No | Page margins, paragraph alignment/spacing, heading/table decoration, and quote-bar width. |
| `palette` | No | Semantic document colors. |

Unknown or duplicate properties are rejected instead of being ignored.

### Typography

All numeric typography values are in points.

| Property | Allowed value |
| --- | --- |
| `latinFont` | Supported font-family name listed below. |
| `cjkFont` | Supported font-family name listed below. |
| `monospaceFont` | Supported font-family name listed below. |
| `bodySize` | `6`–`36` points. |
| `lineHeight` | `8`–`72` points and not smaller than the effective `bodySize`. |
| `headings.h1` … `headings.h6` | `8`–`72` points. |

Supported custom font families are normalized case-insensitively so PDF and DOCX use the same family name:

- Arial
- Cambria
- Courier New
- KaiU
- Malgun Gothic
- Microsoft JhengHei
- MS Gothic
- Noto Sans TC
- PMingLiU
- Segoe UI
- Segoe UI Symbol
- Times New Roman

Font file paths are not accepted. Unsupported family names fail validation rather than silently falling back to a different PDF font.

### Layout

All numeric layout values are in points.

| Property | Allowed value |
| --- | --- |
| `margin` | `18`–`144`; uniform shorthand for all four page margins. Existing version-1 templates keep this behavior. |
| `marginTop` | `18`–`144`; overrides only the top margin. |
| `marginRight` | `18`–`144`; overrides only the right margin. |
| `marginBottom` | `18`–`144`; overrides only the bottom margin. |
| `marginLeft` | `18`–`144`; overrides only the left margin. |
| `blockGap` | `0`–`40`. |
| `firstLineIndent` | `0`–`72`; applied to ordinary body paragraphs, not lists, quotes, or table cells. |
| `centerH1` | `true` or `false`; centers a level-one title when the renderer can keep it on one line. |
| `justifyBody` | `true` or `false`; fully justifies ordinary body paragraphs while leaving headings, lists, quotes, code, and table cells unchanged. |
| `accentH2` | `true` or `false`. |
| `drawH2Bar` | `true` or `false`. |
| `fillTableHeader` | `true` or `false`. |
| `quoteBarWidth` | `0.5`–`12`. |

When `margin` is present, it remains the fallback for every side. A supplied side-specific margin overrides only that side; for example, `"margin": 48, "marginLeft": 72` means 48-point top/right/bottom margins and a 72-point left margin.

Paper size, text-size scaling, and code-block theme remain one-shot conversion choices in the Markdown conversion dialog. An advanced JSON template controls the shared semantic document style rather than those separate conversion controls.

### Palette

The supported semantic colors are `body`, `strong`, `accent`, `softAccent`, and `border`. Every supplied color must use exact `#RRGGBB` notation, for example `#334155`.

### Validation and safety

Clickra validates a selected template before starting the conversion and validates it again when the renderer loads it. The format is intentionally fail-closed:

- DOCX imports are bounded by package and XML-part size limits and must contain `word/styles.xml` plus `word/document.xml`.
- DOCX font families are resolved through Word style inheritance and theme fonts; an explicitly requested font that cannot be kept consistent between PDF and DOCX rejects the import instead of silently substituting typography.
- JSON template files have a maximum size of 64 KiB.
- JSON comments and trailing commas are not accepted.
- Unknown and duplicate properties are rejected.
- Wrong JSON types, unsupported versions or bases, non-finite numbers, and out-of-range values are rejected.
- Font paths, control characters, and font families that cannot be kept consistent between PDF and DOCX are rejected.
- Neither DOCX style extraction nor JSON loading executes macros, commands, scripts, LaTeX, plugins, or network requests.
- Markdown remote images remain offline-only behavior: Clickra does not fetch HTTP(S) images for PDF or DOCX conversion.
