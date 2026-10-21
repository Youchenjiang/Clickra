"""Test Google Translate API endpoints. Merged from test_google_translate/test_endpoints/test_mobile/test_get."""
import requests
import json


ENDPOINTS = {
    "gtx": "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-TW&dt=t",
    "gtx_alt": "https://translate.google.com/translate_a/single?client=gtx&sl=auto&tl=zh-TW&dt=t",
    "chrome": "https://translate.google.com/translate_a/single?client=dict-chrome-ex&sl=auto&tl=zh-TW&dt=t",
    "mobile": "https://translate.google.com/translate_a/t?client=at&sl=auto&tl=zh-TW",
}

MOBILE_HEADERS = {
    "User-Agent": "AndroidTranslate/5.3.0.RC02.130758309-53000263 5.1 phone TRANSLATE_MOBILE_APPLICATION"
}


def test_endpoint(name, url, text="Hello world", mobile=False):
    try:
        headers = MOBILE_HEADERS if mobile else {}
        r = requests.post(url, headers=headers, data={"q": text}, timeout=10)
        print(f"[{name}] Status: {r.status_code}")
        if r.status_code == 200:
            try:
                obj = r.json()
                print(f"  Result: {json.dumps(obj, indent=2, ensure_ascii=False)[:500]}")
            except Exception:
                print(f"  Raw: {r.text[:300]}")
        return r.status_code == 200
    except Exception as e:
        print(f"[{name}] Error: {e}")
        return False


if __name__ == "__main__":
    text = "Hello world"
    for name, url in ENDPOINTS.items():
        test_endpoint(name, url, text, mobile=(name == "mobile"))
        print()
