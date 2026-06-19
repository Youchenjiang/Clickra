"""Validate link/citation alignment. Merged from validate_brackets/validate_text_overlap/page2_link_validate."""
import argparse
from _pdf_utils import validate_link_alignment, SOURCES


def validate(src_pdf, trn_pdf=None):
    if trn_pdf is None:
        trn_pdf = str(src_pdf).replace(".pdf", "_translated.pdf")
    result = validate_link_alignment(src_pdf, trn_pdf)
    print(f"Link alignment: OK={result['ok']} BAD={result['bad']} rate={result['rate']:.1f}%")
    for p, i, e, a in result["samples"]:
        print(f"  Page {p} link[{i}]: expected '{e}' got '{a}'")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Validate link alignment")
    parser.add_argument("--src", "-s", default="2407", help="Source PDF or key")
    parser.add_argument("--trans", "-t", help="Translated PDF or key")
    args = parser.parse_args()
    src = SOURCES.get(args.src, args.src)
    trans = SOURCES.get(args.trans, args.trans) if args.trans else None
    validate(src, trans)
