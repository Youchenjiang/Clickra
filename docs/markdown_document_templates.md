# Markdown document templates

Clickra uses the same document-template model for Markdown → PDF and Markdown → Word. A custom template is a small, data-only JSON file selected for one conversion. It is not installed into Settings, does not execute code, and does not allow arbitrary template scripts.

## Quick example

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

## Root properties

| Property | Required | Meaning |
| --- | --- | --- |
| `version` | Yes | Template format version. The current and only supported value is `1`. |
| `name` | No | Display identity for the imported template. Defaults to the JSON filename without its extension. |
| `base` | No | Built-in template to inherit from: `default`, `minimal`, or `academic`. Defaults to `default`. |
| `typography` | No | Font families, body metrics, and heading sizes. |
| `layout` | No | Page margins, paragraph alignment/spacing, heading/table decoration, and quote-bar width. |
| `palette` | No | Semantic document colors. |

Unknown or duplicate properties are rejected instead of being ignored.

## Typography

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

## Layout

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

Paper size, text-size scaling, and code-block theme remain one-shot conversion choices in the Markdown conversion dialog. A custom JSON template controls the shared semantic document style rather than those separate conversion controls.

## Palette

The supported semantic colors are `body`, `strong`, `accent`, `softAccent`, and `border`. Every supplied color must use exact `#RRGGBB` notation, for example `#334155`.

## Validation and safety

Clickra validates a selected template before starting the conversion and validates it again when the renderer loads it. The format is intentionally fail-closed:

- Maximum file size is 64 KiB.
- JSON comments and trailing commas are not accepted.
- Unknown and duplicate properties are rejected.
- Wrong JSON types, unsupported versions or bases, non-finite numbers, and out-of-range values are rejected.
- Font paths, control characters, and font families that cannot be kept consistent between PDF and DOCX are rejected.
- The JSON contains data only. It cannot execute commands, scripts, LaTeX, plugins, or network requests.
- Markdown remote images remain offline-only behavior: Clickra does not fetch HTTP(S) images for PDF or DOCX conversion.

## Using a custom template

1. Start either Markdown → PDF or Markdown → Word.
2. Open **More options** in the Markdown conversion dialog.
3. Choose the custom-template JSON file.
4. Confirm the selected filename and start the conversion.

The selected path applies only to that conversion. Choosing a template here does not create a persistent template library or change Clickra Settings.
