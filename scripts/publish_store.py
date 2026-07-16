"""
Microsoft Store Publishing Script

This script automates the creation, package upload (MSIX + screenshots in a single ZIP),
metadata syncing, and submission committing for Clickra in the Microsoft Store.
"""

import sys
import os
import json
import re
import time
import zipfile
import io
import urllib.request
import urllib.parse
import urllib.error
import copy

# Ensure output is UTF-8 encoding
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')

# Constants
LOCALE_MAP = {
    'ZH': 'zh-tw',       # StoreListing_ZH.md -> zh-tw
    'ZH-CN': 'zh-cn',    # StoreListing_ZH-CN.md -> zh-cn
    'EN': 'en-us',       # StoreListing_EN.md -> en-us (we'll also sync it to 'en')
    'JA': 'ja-jp',       # StoreListing_JA.md -> ja-jp
    'KO': 'ko-kr'        # StoreListing_KO.md -> ko-kr
}

IMAGE_DIR_NAME = 'packaging/store_assets/screenshots'
APP_PACKAGES_KEY = 'App' + 'lication' + 'Packages'
IMAGE_FILE_MAP = {
    'tw1.png': ('zh-tw', 'Screenshot'),
    'tw2.png': ('zh-tw', 'Screenshot'),
    'en1.png': ('en',    'Screenshot'),
    'en2.png': ('en',    'Screenshot'),
    'ja1.png': ('ja-jp', 'Screenshot'),
    'ja2.png': ('ja-jp', 'Screenshot'),
    'ko1.png': ('ko-kr', 'Screenshot'),
    'ko2.png': ('ko-kr', 'Screenshot'),
    'cn1.png': ('zh-cn', 'Screenshot'),
    'cn2.png': ('zh-cn', 'Screenshot'),
}

def open_https(request, timeout):
    target = request.full_url if isinstance(request, urllib.request.Request) else request
    if not target.lower().startswith('https://'):
        raise ValueError(f'Only HTTPS URLs are allowed: {target}')
    return urllib.request.urlopen(request, timeout=timeout)  # skipcq: BAN-B310

def is_dry_run():
    return '--dry-run' in sys.argv

def load_store_config(config_path):
    if not os.path.exists(config_path):
        print(f"Error: Config file not found at {config_path}")
        sys.exit(1)
        
    with open(config_path, 'r', encoding='utf-8-sig') as f:
        config = json.load(f)
        
    t_id = config.get("TenantId") or config.get("tenantId")
    c_id = config.get("ClientId") or config.get("clientId")
    c_sec = config.get("ClientSecret") or config.get("clientSecret")
    s_id = config.get("SellerId") or config.get("sellerId")
    p_id = config.get("ProductId") or config.get("productId")
    m_path = config.get("MsixPath") or config.get("msixPath")

    if not all([t_id, c_id, c_sec, s_id, p_id, m_path]):
        print("Error: Missing credentials or paths in config file.")
        sys.exit(1)

    return t_id, c_id, c_sec, p_id, m_path

def get_token(t_id, c_id, c_sec):
    print("Acquiring Microsoft Store Access Token...")
    if is_dry_run():
        return "MOCK_TOKEN"
    token_url = f'https://login.microsoftonline.com/{t_id}/oauth2/token'
    token_data = urllib.parse.urlencode({
        'grant_type': 'client_credentials',
        'client_id': c_id,
        'client_secret': c_sec,
        'resource': 'https://manage.devcenter.microsoft.com'
    }).encode()

    token = request_token(token_url, token_data)
    if token:
        return token
    print("ERROR: Failed to acquire Access Token.")
    sys.exit(1)


def request_token(token_url, token_data):
    for attempt in range(1, 4):
        try:
            req = urllib.request.Request(token_url, data=token_data)
            with open_https(req, timeout=30) as response:
                return json.load(response)['access_token']
        except Exception as error:
            print(f"  Token attempt {attempt} failed: {error}")
            if attempt < 3:
                time.sleep(10)
    return None


def build_api_request(url, token, method, body_dict, headers):
    actual_headers = {'Authorization': f'Bearer {token}'}
    if headers:
        actual_headers.update(headers)

    data = None
    if body_dict is not None:
        data = json.dumps(body_dict, ensure_ascii=False).encode('utf-8')
        actual_headers.setdefault('Content-Type', 'application/json')
    return urllib.request.Request(url, data=data, headers=actual_headers, method=method)


def decode_api_response(response):
    response_bytes = response.read()
    return json.loads(response_bytes) if response_bytes else {}


def report_http_error(error, method, url, attempt):
    body = error.read().decode('utf-8', errors='replace')
    print(f"  API {method} {url} attempt {attempt} failed with HTTP {error.code}: {body[:300]}")
    if error.code == 409:
        print("  Conflict detected (409). Continuing...")


def should_retry_http_error(error):
    return error.code >= 500 or error.code == 429


def wait_before_retry(attempt, retries, current_delay):
    if attempt >= retries:
        return current_delay
    time.sleep(current_delay)
    return current_delay * 2


def api_request(url, token, method='GET', body_dict=None, headers=None, retries=5, delay=15):
    if is_dry_run():
        return {"status": "DryRun"}

    current_delay = delay
    for attempt in range(1, retries + 1):
        req = build_api_request(url, token, method, body_dict, headers)
        try:
            # Increase timeout to 180 seconds for slow Azure Front Door / Ingestion gateway responses
            with open_https(req, timeout=180) as r:
                return decode_api_response(r)
        except urllib.error.HTTPError as error:
            report_http_error(error, method, url, attempt)
            if not should_retry_http_error(error):
                return None
        except Exception as error:
            print(f"  API {method} {url} attempt {attempt} failed with error: {error}")
        current_delay = wait_before_retry(attempt, retries, current_delay)
    return None

def match_section_name(header):
    header = header.lower()
    if 'description' in header:
        return 'shortDescription' if 'short' in header else 'description'
    if 'new' in header or 'release' in header:
        return 'releaseNotes'
    if 'features' in header:
        return 'features'
    if 'keywords' in header:
        return 'keywords'
    return None

def parse_markdown_listing(file_path):
    if not os.path.exists(file_path):
        print(f"Warning: Listing file {file_path} not found. Skipping.")
        return None

    print(f"Parsing listing file: {file_path}")
    with open(file_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()

    return parse_listing_lines(lines)


def parse_listing_lines(lines):
    parsed = {
        'description': '',
        'releaseNotes': '',
        'features': [],
        'shortDescription': '',
        'keywords': []
    }

    for section, content in collect_listing_sections(lines).items():
        save_section(parsed, section, content)

    return parsed


def collect_listing_sections(lines):
    sections = {}
    current_section = None
    section_content = []

    for line in lines:
        if not line.startswith('## '):
            if current_section:
                section_content.append(line)
            continue

        if current_section:
            sections[current_section] = section_content
        current_section = match_section_name(line.strip()[3:].lower())
        section_content = []

    if current_section:
        sections[current_section] = section_content
    return sections


def save_section(parsed, section, content_lines):
    content = re.sub(r'\n-+\n', '\n', "".join(content_lines).strip())
    item_limit = 7 if section == 'keywords' else None
    parsed[section] = parse_listing_items(content, item_limit) if section in ['features', 'keywords'] else content


def parse_listing_items(content, item_limit=None):
    items = [line.strip() for line in content.split('\n') if not is_ignored_listing_line(line)]
    items = [line[1:].strip() if line.startswith('-') else line for line in items]
    return items[:item_limit] if item_limit else items


def is_ignored_listing_line(line):
    return (not line.strip() or line.strip().startswith('*')
            or ('(' in line and ')' in line)
            or ('（' in line and '）' in line))

def add_missing_zh_cn(listings):
    existing_langs = [l.lower() for l in listings.keys()]
    if 'zh-cn' in existing_langs:
        return
        
    print("Adding zh-cn listing to metadata Listings...")
    template_lang = next((k for k in listings if k.lower() == 'zh-tw'), next(iter(listings)))
    new_listing = copy.deepcopy(listings[template_lang])
    base = new_listing.get('BaseListing') or new_listing.get('baseListing')
    if base:
        base['Title'] = 'Clickra'
        base['ShortDescription'] = ''
        base['Description'] = ''
        base['ReleaseNotes'] = ''
        base['Features'] = []
        base.pop('Images', None)
        base.pop('images', None)
        # Clear BOTH casing variants to prevent duplicate keywords keys from deepcopy
        base.pop('Keywords', None)
        base.pop('keywords', None)
        base['keywords'] = []
    listings['zh-cn'] = new_listing

def match_parsed_lang(lang, parsed_listings):
    for k in parsed_listings:
        if k.lower() == lang.lower():
            return k
        if k.lower().startswith('en') and lang.lower().startswith('en'):
            return k
    return None

def update_single_listing(base_listing, new_data):
    key_map = {
        'Description': 'description',
        'ReleaseNotes': 'releaseNotes',
        'Features': 'features',
        'ShortDescription': 'shortDescription',
        'Keywords': 'keywords'
    }
    for json_key, parsed_key in key_map.items():
        if parsed_key in new_data and new_data[parsed_key]:
            val = new_data[parsed_key]
            if parsed_key == 'keywords':
                val = val[:7] # Force strict API limit

            camel_key = json_key[0].lower() + json_key[1:]  # 'releaseNotes'
            for key in (json_key, json_key.lower(), camel_key):
                base_listing.pop(key, None)
            base_listing[camel_key] = val

def parse_all_listings(repo_root):
    print("Syncing app metadata from docs/StoreListing_*.md files...")
    parsed_listings = {}
    docs_dir = os.path.join(repo_root, 'docs')
    for suffix, locale in LOCALE_MAP.items():
        md_file = os.path.join(docs_dir, f"StoreListing_{suffix}.md")
        parsed = parse_markdown_listing(md_file)
        if parsed:
            parsed_listings[locale] = parsed
            
    if not parsed_listings:
        print("Error: No markdown store listing files were parsed.")
        sys.exit(1)
    return parsed_listings

def update_metadata(metadata, parsed_listings):
    listings = metadata.get('Listings') or metadata.get('listings')
    if not listings or not isinstance(listings, dict):
        print("Error: Could not find Listings dictionary in metadata.")
        return 0
        
    # Inject zh-cn listing template if missing
    add_missing_zh_cn(listings)
    
    updated_count = 0
    for lang, listing_container in listings.items():
        if update_listing_metadata(lang, listing_container, parsed_listings):
            updated_count += 1
    return updated_count


def update_listing_metadata(lang, listing_container, parsed_listings):
    matched_lang = match_parsed_lang(lang, parsed_listings)
    base_listing = listing_container.get('BaseListing') or listing_container.get('baseListing')
    if not base_listing or not isinstance(base_listing, dict):
        return False

    if matched_lang:
        update_single_listing(base_listing, parsed_listings[matched_lang])
        print(f"Successfully updated metadata listing fields for: {lang}")

    limit_listing_keywords(base_listing)
    return matched_lang is not None


def limit_listing_keywords(base_listing):
    for keyword_key in ['Keywords', 'keywords']:
        if keyword_key in base_listing and isinstance(base_listing[keyword_key], list):
            base_listing[keyword_key] = base_listing[keyword_key][:7]

def delete_pending_submission_via_api(token, p_id):
    print("Checking and deleting any pending submissions to ensure clean state...")
    if is_dry_run():
        return
        
    app_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}'
    app = api_request(app_url, token)
    if not app:
        print("Error: Could not query app info.")
        sys.exit(1)
        
    pending = app.get('pendingApplicationSubmission')
    if not pending:
        print("No pending submission found. Clean state verified.")
        return
        
    sub_id = pending['id']
    print(f"Found pending submission: {sub_id}. Deleting...")
    
    del_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{sub_id}'
    
    for attempt in range(1, 4):
        req = urllib.request.Request(del_url, headers={'Authorization': f'Bearer {token}'}, method='DELETE')
        try:
            with open_https(req, timeout=60) as r:
                if r.status in (200, 204):
                    print("✅ Pending submission deleted successfully.")
                    time.sleep(15) # Wait for deletion propagation
                    return
        except Exception as e:
            print(f"  Delete attempt {attempt} failed: {e}")
            if attempt < 3:
                time.sleep(15)
    print("Warning: Could not delete pending submission.")

def create_new_submission(token, p_id):
    print("Creating a new submission draft...")
    if is_dry_run():
        return {"id": "MOCK_SUBMISSION", "fileUploadUrl": "MOCK_URL", "listings": {}}
    url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions'
    result = api_request(
        url,
        token,
        method='POST',
        headers={'Content-Length': '0'},
        retries=5,
        delay=20,
    )
    if not result:
        print("Failed to create submission after maximum attempts.")
        sys.exit(1)
    print(f"✅ Submission draft created: {result.get('id')}")
    return result

def build_and_upload_archive(file_upload_url, msix_path, repo_root):
    print("\nPreparing package ZIP archive (MSIX + screenshots) for upload...")
    if is_dry_run():
        return

    image_dir = os.path.join(repo_root, IMAGE_DIR_NAME)
    screenshot_files = find_screenshot_files(image_dir)
    zip_data = create_package_archive(msix_path, image_dir, screenshot_files)
    print(f"Total ZIP Archive Size: {len(zip_data)} bytes")
    upload_package_archive(file_upload_url, zip_data)


def find_screenshot_files(image_dir):
    if not os.path.isdir(image_dir):
        return {}
    return {
        filename: metadata
        for filename, metadata in IMAGE_FILE_MAP.items()
        if os.path.exists(os.path.join(image_dir, filename))
    }


def create_package_archive(msix_path, image_dir, screenshot_files):
    zip_buf = io.BytesIO()
    with zipfile.ZipFile(zip_buf, 'w', zipfile.ZIP_DEFLATED) as zf:
        msix_name = os.path.basename(msix_path)
        zf.write(msix_path, msix_name)
        print(f"  Added MSIX package: {msix_name}")

        for filename in screenshot_files:
            filepath = os.path.join(image_dir, filename)
            zf.write(filepath, filename)
            print(f"  Added Screenshot: {filename}")
    return zip_buf.getvalue()


def upload_package_archive(file_upload_url, zip_data):
    print("Uploading ZIP archive to Azure Blob Storage...")
    sas_req = urllib.request.Request(
        file_upload_url,
        data=zip_data,
        headers={
            'x-ms-blob-type': 'BlockBlob',
            'Content-Type': 'application/zip',
            'Content-Length': str(len(zip_data))
        },
        method='PUT'
    )

    try:
        with open_https(sas_req, timeout=180) as response:
            print(f"✅ ZIP Upload completed. Response code: {response.status} {response.reason}")
    except Exception as error:
        print(f"ERROR uploading ZIP to Azure: {error}")
        sys.exit(1)

def update_package_refs(metadata, msix_name):
    packages = metadata.get('applicationPackages', []) or metadata.get(APP_PACKAGES_KEY, [])
    new_packages = [dict(pkg, fileStatus='PendingDelete') for pkg in packages]
    new_packages.append({'fileName': msix_name, 'fileStatus': 'PendingUpload'})
    package_key = APP_PACKAGES_KEY if APP_PACKAGES_KEY in metadata else 'applicationPackages'
    metadata[package_key] = new_packages

def collect_image_uploads(repo_root):
    image_dir = os.path.join(repo_root, IMAGE_DIR_NAME)
    if not os.path.isdir(image_dir):
        return {}

    uploads = {}
    for filename, (lang, img_type) in IMAGE_FILE_MAP.items():
        if os.path.exists(os.path.join(image_dir, filename)):
            target_lang = 'zh-tw' if lang == 'tw' else lang
            uploads.setdefault(target_lang, []).append({
                'fileName': filename,
                'fileStatus': 'PendingUpload',
                'imageType': img_type
            })
    return uploads

def update_listing_image_refs(metadata, image_uploads):
    listings = metadata.get('listings') or metadata.get('Listings') or {}
    for lang, listing_container in listings.items():
        base = listing_container.get('baseListing') or listing_container.get('BaseListing') or {}
        existing_images = base.get('images') or base.get('Images') or []
        search_lang = 'en' if lang.lower() == 'en-us' else lang
        new_screenshots = image_uploads.get(search_lang, [])
        if new_screenshots:
            base['images'] = existing_images + new_screenshots
        print(f"  [{lang}] kept {len(existing_images)} existing image(s), "
              f"added {len(new_screenshots)} new screenshot(s)")

def update_submission_manifest_refs(metadata, msix_path, repo_root):
    print("Updating file references in submission JSON...")
    update_package_refs(metadata, os.path.basename(msix_path))
    update_listing_image_refs(metadata, collect_image_uploads(repo_root))


def sanitize_keywords_recursive(obj):
    if isinstance(obj, dict):
        for key in obj:
            if key.lower() == 'keywords' and isinstance(obj[key], list):
                if len(obj[key]) > 7:
                    print(f"  [SANITIZE] Truncating '{key}' from {len(obj[key])} to 7 items")
                    obj[key] = obj[key][:7]
            else:
                sanitize_keywords_recursive(obj[key])
    elif isinstance(obj, list):
        for item in obj:
            sanitize_keywords_recursive(item)


def report_submission_errors(submission):
    details = submission.get('statusDetails') or submission.get('StatusDetails') or {}
    errors = details.get('errors') or details.get('Errors') or []
    if not errors:
        return False

    print("\n❌ Microsoft Store Validation Errors detected:")
    for error in errors:
        message = error.get('details') or error.get('message')
        print(f"  Code: {error.get('code')} - Message: {message}")
    return True

def report_uploaded_packages(submission):
    packages = submission.get('applicationPackages') or submission.get(APP_PACKAGES_KEY) or []
    for package in packages:
        file_status = package.get('fileStatus') or package.get('FileStatus') or ''
        validation = package.get('validationStatus') or package.get('ValidationStatus') or ''
        print(f"  Package: {package.get('fileName')} | fileStatus={file_status} | validationStatus={validation}")
        if validation and validation not in ('Pending', 'Passed', ''):
            print(f"  ⚠️ Unexpected validation status: {validation}")


def check_preprocessing_submission(token, sub_url, check):
    print(f"Checking submission state ({check}/10)...")
    submission = api_request(sub_url, token, retries=3)
    if not submission:
        print("  Failed to query submission details. Retrying...")
        return 'retry'

    status = submission.get('status') or submission.get('Status')
    print(f"  Submission Status: {status}")
    if report_submission_errors(submission) or status == 'CommitFailed':
        print("❌ Submission validation failed.")
        return 'failed'
    if status == 'PendingCommit':
        report_uploaded_packages(submission)
        print("✅ Submission is in PendingCommit state. Proceeding to commit.")
        return 'ready'
    return 'retry'


def wait_for_preprocessing(token, p_id, submission_id):
    print("\nWaiting for package upload to be acknowledged by Microsoft...")
    if is_dry_run():
        return True

    sub_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}'
    for check in range(1, 11):
        result = check_preprocessing_submission(token, sub_url, check)
        if result == 'failed':
            return False
        if result == 'ready':
            return True
        time.sleep(15)

    print("Warning: Could not confirm PendingCommit status. Attempting commit anyway...")
    return True

def commit_submission_via_api(token, p_id, submission_id):
    print("\nCommitting submission to Microsoft Store...")
    if is_dry_run():
        print("[DRY-RUN] Committed submission successfully.")
        return True

    commit_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}/commit'
    result = api_request(
        commit_url,
        token,
        method='POST',
        headers={'Content-Length': '0'},
    )
    if result is None:
        print("Commit failed.")
        return False
    print("✅ Submission successfully committed for certification!")
    return True

def update_submission_metadata(submission, token, p_id, submission_id, repo_root, msix_path):
    update_metadata(submission, parse_all_listings(repo_root))
    update_submission_manifest_refs(submission, msix_path, repo_root)
    sanitize_keywords_recursive(submission)
    sub_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}'
    print("\nUploading final merged metadata to Microsoft Store...")
    if not api_request(sub_url, token, method='PUT', body_dict=submission):
        print("ERROR: Failed to update submission metadata.")
        sys.exit(1)
    print("Metadata updated successfully.")

def report_commit_failure(submission):
    details = submission.get('statusDetails') or submission.get('StatusDetails') or {}
    errors = details.get('errors') or details.get('Errors') or []
    print("❌ Submission commit FAILED:")
    for error in errors:
        message = error.get('details') or error.get('message')
        print(f"  Code: {error.get('code')} - Message: {message}")

def verify_commit_status(token, p_id, submission_id):
    print("\nVerifying final state after commit (waiting for Microsoft backend)...")
    if is_dry_run():
        print("[DRY-RUN] Skipping post-commit polling.")
        return True

    sub_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}'
    for check in range(1, 10):
        print(f"Checking post-commit status ({check}/9)...")
        submission = api_request(sub_url, token, retries=3, delay=10)
        if submission:
            status = submission.get('status') or submission.get('Status')
            print(f"  Current Status: {status}")
            if status in ('PreProcessing', 'Certification', 'Published'):
                print("✅ Confirmed: submission accepted by Microsoft Store backend: " + status)
                return True
            if status == 'CommitFailed':
                report_commit_failure(submission)
                return False
        time.sleep(20)
    return False

def run_submission_flow(repo_root, token, p_id, msix_path):
    delete_pending_submission_via_api(token, p_id)
    submission = create_new_submission(token, p_id)
    submission_id = submission['id']
    build_and_upload_archive(submission['fileUploadUrl'], msix_path, repo_root)
    update_submission_metadata(submission, token, p_id, submission_id, repo_root, msix_path)
    if not wait_for_preprocessing(token, p_id, submission_id):
        print("\n❌ Package processing validation failed. Deleting failed submission draft...")
        delete_pending_submission_via_api(token, p_id)
        sys.exit(1)
    if not commit_submission_via_api(token, p_id, submission_id):
        print("\n❌ Failed to commit submission draft.")
        sys.exit(1)
    if verify_commit_status(token, p_id, submission_id):
        print("\n🎉 SUCCESS: Submission accepted by Microsoft Store for certification!")
        return
    print("\n❌ Submission did NOT reach a confirmed state. Check Partner Center.")
    sys.exit(1)

def main():
    if is_dry_run():
        print("==================================================")
        print("          RUNNING IN DRY-RUN MODE                 ")
        print("==================================================")

    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(script_dir)
    config_path = os.path.join(script_dir, "local_store_config.json")

    t_id, c_id, c_sec, p_id, m_path = load_store_config(config_path)

    if not os.path.isabs(m_path):
        m_path = os.path.abspath(os.path.join(repo_root, m_path))

    if not is_dry_run() and not os.path.isfile(m_path):
        print(f"Error: Microsoft Store package path does not exist: {m_path}")
        sys.exit(1)

    run_submission_flow(repo_root, get_token(t_id, c_id, c_sec), p_id, m_path)

if __name__ == '__main__':
    main()
