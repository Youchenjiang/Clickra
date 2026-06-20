import requests

url = "https://translate.google.com/translate_a/t?client=at&sl=auto&tl=zh-TW"
headers = {
    "User-Agent": "AndroidTranslate/5.3.0.RC02.130758309-53000263 5.1 phone TRANSLATE_MOBILE_APPLICATION"
}

paragraphs = [
    f"Paragraph {i}: This is some sample text to translate. We want to see if the mobile API handles many queries at once."
    for i in range(30)
]

data = [("q", p) for p in paragraphs]

try:
    r = requests.post(url, headers=headers, data=data, timeout=5)
    print("Mobile Status:", r.status_code)
    if r.status_code == 200:
        res = r.json()
        print(f"Returned {len(res)} results.")
        print("First 3 results:")
        for item in res[:3]:
            print(item)
except Exception as e:
    print("Mobile Error:", e)
