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

DRY_RUN = False

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
        
    return t_id, c_id, c_sec, s_id, p_id, m_path

def get_token(t_id, c_id, c_sec):
    print("Acquiring Microsoft Store Access Token...")
    if DRY_RUN:
        return "MOCK_TOKEN"
    token_url = f'https://login.microsoftonline.com/{t_id}/oauth2/token'
    token_data = urllib.parse.urlencode({
        'grant_type': 'client_credentials',
        'client_id': c_id,
        'client_secret': c_sec,
        'resource': 'https://manage.devcenter.microsoft.com'
    }).encode()
    
    for attempt in range(1, 4):
        try:
            req = urllib.request.Request(token_url, data=token_data)
            with urllib.request.urlopen(req, timeout=30) as r:
                return json.loads(r.read())['access_token']
        except Exception as e:
            print(f"  Token attempt {attempt} failed: {e}")
            if attempt < 3:
                time.sleep(10)
    print("ERROR: Failed to acquire Access Token.")
    sys.exit(1)

def api_request(url, token, method='GET', body_dict=None, headers=None, retries=5, delay=15):
    if DRY_RUN:
        return {"status": "DryRun"}
        
    actual_headers = {
        'Authorization': f'Bearer {token}'
    }
    if headers:
        actual_headers.update(headers)
        
    data = None
    if body_dict is not None:
        data = json.dumps(body_dict, ensure_ascii=False).encode('utf-8')
        if 'Content-Type' not in actual_headers:
            actual_headers['Content-Type'] = 'application/json'

    current_delay = delay
    for attempt in range(1, retries + 1):
        req = urllib.request.Request(url, data=data, headers=actual_headers, method=method)
        try:
            # Increase timeout to 180 seconds for slow Azure Front Door / Ingestion gateway responses
            with urllib.request.urlopen(req, timeout=180) as r:
                resp_bytes = r.read()
                if resp_bytes:
                    return json.loads(resp_bytes)
                return {}
        except urllib.error.HTTPError as e:
            body = e.read().decode('utf-8', errors='replace')
            print(f"  API {method} {url} attempt {attempt} failed with HTTP {e.code}: {body[:300]}")
            if e.code == 409:
                print("  Conflict detected (409). Continuing...")
            if attempt < retries:
                time.sleep(current_delay)
                current_delay *= 2 # Exponential backoff
        except Exception as e:
            print(f"  API {method} {url} attempt {attempt} failed with error: {e}")
            if attempt < retries:
                time.sleep(current_delay)
                current_delay *= 2 # Exponential backoff
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

    parsed = {
        'description': '',
        'releaseNotes': '',
        'features': [],
        'shortDescription': '',
        'keywords': []
    }

    current_section = None
    section_content = []

    for line in lines:
        stripped = line.strip()
        if line.startswith('## '):
            if current_section:
                save_section(parsed, current_section, section_content)
            header = stripped[3:].lower()
            current_section = match_section_name(header)
            section_content = []
        else:
            if current_section:
                section_content.append(line)

    if current_section:
        save_section(parsed, current_section, section_content)

    return parsed

def save_section(parsed, section, content_lines):
    content = "".join(content_lines).strip()
    content = re.sub(r'\n-+\n', '\n', content)
    
    if section in ['features', 'keywords']:
        items = []
        for line in content.split('\n'):
            line = line.strip()
            # Skip empty lines, lines containing markdown hints like *(Max 7)*, or bracket comments
            if not line or line.startswith('*') or ('(' in line and ')' in line) or ('（' in line and '）' in line):
                continue
            if line.startswith('-'):
                items.append(line[1:].strip())
            else:
                items.append(line)
        if section == 'keywords':
            items = items[:7] # Microsoft Store API strictly enforces maximum 7 keywords
        parsed[section] = items
    else:
        parsed[section] = content

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

            # The Partner Center API returns camelCase keys (e.g. 'releaseNotes')
            # but may also use PascalCase ('ReleaseNotes'). Check all variants
            # to find the existing key and update in-place.
            camel_key = json_key[0].lower() + json_key[1:]  # 'releaseNotes'
            if json_key in base_listing:
                base_listing[json_key] = val
            elif camel_key in base_listing:
                base_listing[camel_key] = val
            elif json_key.lower() in base_listing:
                base_listing[json_key.lower()] = val
            else:
                # Key doesn't exist yet — write with camelCase (API convention)
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
        matched_lang = match_parsed_lang(lang, parsed_listings)
        base_listing = listing_container.get('BaseListing') or listing_container.get('baseListing')
        if not base_listing or not isinstance(base_listing, dict):
            continue
            
        if matched_lang:
            update_single_listing(base_listing, parsed_listings[matched_lang])
            updated_count += 1
            print(f"Successfully updated metadata listing fields for: {lang}")
            
        # Hard constraint check for ALL listings (including unmodified or cloned ones)
        for kw_key in ['Keywords', 'keywords']:
            if kw_key in base_listing and isinstance(base_listing[kw_key], list):
                base_listing[kw_key] = base_listing[kw_key][:7]
                
    return updated_count

def delete_pending_submission_via_api(token, p_id):
    print("Checking and deleting any pending submissions to ensure clean state...")
    if DRY_RUN:
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
            with urllib.request.urlopen(req, timeout=60) as r:
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
    if DRY_RUN:
        return {"id": "MOCK_SUBMISSION", "fileUploadUrl": "MOCK_URL", "listings": {}}
    url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions'
    
    req = urllib.request.Request(url, data=b'', headers={
        'Authorization': f'Bearer {token}',
        'Content-Type': 'application/json',
        'Content-Length': '0'
    }, method='POST')
    
    max_attempts = 5
    for attempt in range(1, max_attempts + 1):
        try:
            print(f"  Attempt {attempt}/{max_attempts} to create submission...")
            with urllib.request.urlopen(req, timeout=120) as r:
                res = json.loads(r.read())
                print(f"✅ Submission draft created: {res.get('id')}")
                return res
        except Exception as e:
            print(f"  Attempt {attempt} failed: {e}")
            if attempt < max_attempts:
                print("  Waiting 20 seconds before retry...")
                time.sleep(20)
    print("Failed to create submission after maximum attempts.")
    sys.exit(1)

def build_and_upload_archive(file_upload_url, msix_path, repo_root):
    print("\nPreparing package ZIP archive (MSIX + screenshots) for upload...")
    if DRY_RUN:
        return
        
    image_dir = os.path.join(repo_root, IMAGE_DIR_NAME)
    
    # Locate all screenshots locally
    existing_images = {}
    if os.path.isdir(image_dir):
        existing_images = {f: v for f, v in IMAGE_FILE_MAP.items()
                           if os.path.exists(os.path.join(image_dir, f))}
                           
    zip_buf = io.BytesIO()
    with zipfile.ZipFile(zip_buf, 'w', zipfile.ZIP_DEFLATED) as zf:
        # 1. Add MSIX package
        msix_name = os.path.basename(msix_path)
        zf.write(msix_path, msix_name)
        print(f"  Added MSIX package: {msix_name}")
        
        # 2. Add Screenshots
        for filename in existing_images:
            filepath = os.path.join(image_dir, filename)
            zf.write(filepath, filename)
            print(f"  Added Screenshot: {filename}")
            
    zip_data = zip_buf.getvalue()
    print(f"Total ZIP Archive Size: {len(zip_data)} bytes")

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
        with urllib.request.urlopen(sas_req, timeout=180) as r:
            print(f"✅ ZIP Upload completed. Response code: {r.status} {r.reason}")
    except Exception as e:
        print(f"ERROR uploading ZIP to Azure: {e}")
        sys.exit(1)

def update_submission_manifest_refs(metadata, msix_path, repo_root):
    print("Updating file references in submission JSON...")
    # Update package references
    packages = metadata.get('applicationPackages', []) or metadata.get('ApplicationPackages', [])
    msix_name = os.path.basename(msix_path)

    # Microsoft Store API REQUIRES keeping existing package entries.
    # Mark all existing packages as PendingDelete (preserve their full fields + IDs),
    # then add a single new PendingUpload entry for our new package.
    new_packages = []
    for pkg in packages:
        # Preserve the entire original entry (including 'id', 'fileId', etc.)
        # and set fileStatus to PendingDelete
        pkg_copy = dict(pkg)
        pkg_copy['fileStatus'] = 'PendingDelete'
        new_packages.append(pkg_copy)

    # Append our new package
    new_packages.append({
        'fileName': msix_name,
        'fileStatus': 'PendingUpload',
    })

    if 'ApplicationPackages' in metadata:
        metadata['ApplicationPackages'] = new_packages
    else:
        metadata['applicationPackages'] = new_packages
        
    # Update image references for listings
    image_dir = os.path.join(repo_root, IMAGE_DIR_NAME)
    existing_images = {}
    if os.path.isdir(image_dir):
        existing_images = {f: v for f, v in IMAGE_FILE_MAP.items()
                           if os.path.exists(os.path.join(image_dir, f))}

    lang_new_imgs = {}
    for filename, (lang, img_type) in existing_images.items():
        # Handle 'tw' mapping to 'zh-tw'
        target_lang = 'zh-tw' if lang == 'tw' else lang
        lang_new_imgs.setdefault(target_lang, []).append({
            'fileName': filename,
            'fileStatus': 'PendingUpload',
            'imageType': img_type
        })

    listings = metadata.get('listings') or metadata.get('Listings') or {}
    for lang, listing_container in listings.items():
        base = listing_container.get('baseListing') or listing_container.get('BaseListing') or {}

        # Strategy: keep ALL existing images untouched and APPEND new PendingUpload
        # screenshots.  The Partner Center API silently ignores PendingDelete on
        # screenshots that were Uploaded in a previous published submission, so we
        # cannot replace them.  Instead we add our new screenshots alongside the old
        # ones; the old ones can be cleaned up manually in Partner Center if needed.
        existing_imgs = base.get('images') or base.get('Images') or []

        # If 'en-us', fallback to 'en' screenshots
        search_lang = 'en' if lang.lower() == 'en-us' else lang
        new_screenshots = lang_new_imgs.get(search_lang, [])
        if new_screenshots:
            base['images'] = existing_imgs + new_screenshots
            print(f"  [{lang}] kept {len(existing_imgs)} existing image(s), "
                  f"added {len(new_screenshots)} new screenshot(s)")
        else:
            print(f"  [{lang}] kept {len(existing_imgs)} existing image(s)")

def wait_for_preprocessing(token, p_id, submission_id):
    print("\nWaiting for package upload to be acknowledged by Microsoft...")
    if DRY_RUN:
        return True

    sub_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}'

    max_checks = 10
    check_interval = 15

    for check in range(1, max_checks + 1):
        print(f"Checking submission state ({check}/{max_checks})...")
        sub = api_request(sub_url, token, retries=3)
        if not sub:
            print("  Failed to query submission details. Retrying...")
            time.sleep(check_interval)
            continue

        status = sub.get('status') or sub.get('Status')
        sd = sub.get('statusDetails') or sub.get('StatusDetails') or {}
        errors = sd.get('errors') or sd.get('Errors') or []

        print(f"  Submission Status: {status}")

        if errors:
            print("\n❌ Microsoft Store Validation Errors detected:")
            for err in errors:
                print(f"  Code: {err.get('code')} - Message: {err.get('details') or err.get('message')}")
            return False

        if status == 'CommitFailed':
            print("❌ Submission entered CommitFailed status.")
            return False

        # PendingCommit = package upload acknowledged, ready to commit
        if status == 'PendingCommit':
            # Double-check: verify packages are actually uploaded
            packages = sub.get('applicationPackages') or sub.get('ApplicationPackages') or []
            for pkg in packages:
                file_status = pkg.get('fileStatus') or pkg.get('FileStatus') or ''
                val_status = pkg.get('validationStatus') or pkg.get('ValidationStatus') or ''
                print(f"  Package: {pkg.get('fileName')} | fileStatus={file_status} | validationStatus={val_status}")
                if val_status and val_status not in ('Pending', 'Passed', ''):
                    print(f"  ⚠️ Unexpected validation status: {val_status}")

            print("✅ Submission is in PendingCommit state. Proceeding to commit.")
            return True

        time.sleep(check_interval)

    print("Warning: Could not confirm PendingCommit status. Attempting commit anyway...")
    return True

def commit_submission_via_api(token, p_id, submission_id):
    print("\nCommitting submission to Microsoft Store...")
    if DRY_RUN:
        print("[DRY-RUN] Committed submission successfully.")
        return True
        
    commit_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}/commit'
    
    # POST empty body to commit
    req = urllib.request.Request(
        commit_url,
        data=b'{}',
        headers={
            'Authorization': f'Bearer {token}',
            'Content-Type': 'application/json',
            'Content-Length': '2'
        },
        method='POST'
    )
    
    try:
        # Increase timeout to 180 seconds to survive heavy asynchronous commits
        with urllib.request.urlopen(req, timeout=180) as r:
            res_body = r.read().decode('utf-8', errors='replace')
            print(f"Commit response ({r.status}): {res_body}")
            if r.status in (200, 202):
                print("✅ Submission successfully committed for certification!")
                return True
    except urllib.error.HTTPError as e:
        body = e.read().decode('utf-8', errors='replace')
        print(f"Commit failed with HTTP {e.code}: {body}")
    except Exception as e:
        print(f"Commit failed: {e}")
        
    return False

def main():
    global DRY_RUN
    if "--dry-run" in sys.argv:
        DRY_RUN = True
        print("==================================================")
        print("          RUNNING IN DRY-RUN MODE                 ")
        print("==================================================")

    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(script_dir)
    config_path = os.path.join(script_dir, "local_store_config.json")
    
    t_id, c_id, c_sec, s_id, p_id, m_path = load_store_config(config_path)
    
    if not os.path.isabs(m_path):
        m_path = os.path.abspath(os.path.join(repo_root, m_path))
        
    if not DRY_RUN and not os.path.isfile(m_path):
        print(f"Error: Microsoft Store package path does not exist: {m_path}")
        sys.exit(1)
        
    token = get_token(t_id, c_id, c_sec)
    
    # 1. Clear any failed/pending submission first to ensure clean state
    delete_pending_submission_via_api(token, p_id)
    
    # 2. Create new submission draft (clones the last published submission)
    submission = create_new_submission(token, p_id)
    submission_id = submission['id']
    file_upload_url = submission['fileUploadUrl']
    
    # 3. Build ZIP package with MSIX and screenshots, and upload to Azure Blob SAS URL
    build_and_upload_archive(file_upload_url, m_path, repo_root)
    
    # 4. Parse local Markdown store listing files
    parsed_listings = parse_all_listings(repo_root)
    
    # 5. Merge local listing fields into current submission metadata
    update_metadata(submission, parsed_listings)
    
    # 6. Update file references in submission JSON
    update_submission_manifest_refs(submission, m_path, repo_root)
    
    # 7. Final global sanitization pass: recursively enforce ≤7 keywords everywhere in JSON
    def sanitize_keywords_recursive(obj):
        if isinstance(obj, dict):
            for k in list(obj.keys()):
                if k.lower() == 'keywords' and isinstance(obj[k], list):
                    if len(obj[k]) > 7:
                        print(f"  [SANITIZE] Truncating '{k}' from {len(obj[k])} to 7 items")
                        obj[k] = obj[k][:7]
                else:
                    sanitize_keywords_recursive(obj[k])
        elif isinstance(obj, list):
            for item in obj:
                sanitize_keywords_recursive(item)
    sanitize_keywords_recursive(submission)

    # Upload updated metadata back to Microsoft Store
    sub_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}'
    print("\nUploading final merged metadata to Microsoft Store...")
    put_res = api_request(sub_url, token, method='PUT', body_dict=submission)
    if not put_res:
        print("ERROR: Failed to update submission metadata.")
        sys.exit(1)
    print("Metadata updated successfully.")
    
    # 8. Wait for package preprocessing (FileStatus of package goes from PendingUpload to Uploaded)
    success = wait_for_preprocessing(token, p_id, submission_id)
    if not success:
        print("\n❌ Package processing validation failed. Deleting failed submission draft...")
        delete_pending_submission_via_api(token, p_id)
        sys.exit(1)
        
    # 9. Finally, commit the submission to submit for certification
    commit_success = commit_submission_via_api(token, p_id, submission_id)
    if not commit_success:
        print("\n❌ Failed to commit submission draft.")
        sys.exit(1)
        
    # 10. Post-commit verification loop (up to 3 minutes)
    # CommitStarted = async processing started, NOT success.
    # Must wait for PreProcessing or Certification to confirm real acceptance.
    print("\nVerifying final state after commit (waiting for Microsoft backend)...")
    sub_url = f'https://manage.devcenter.microsoft.com/v1.0/my/applications/{p_id}/submissions/{submission_id}'
    state_confirmed = False

    for check in range(1, 10):
        print(f"Checking post-commit status ({check}/9)...")
        sub_info = api_request(sub_url, token, retries=3, delay=10)
        if sub_info:
            status = sub_info.get('status') or sub_info.get('Status')
            print(f"  Current Status: {status}")

            if status in ('PreProcessing', 'Certification', 'Published'):
                print("✅ Confirmed: submission accepted by Microsoft Store backend: " + status)
                state_confirmed = True
                break

            if status == 'CommitFailed':
                sd = sub_info.get('statusDetails') or sub_info.get('StatusDetails') or {}
                errors = sd.get('errors') or sd.get('Errors') or []
                print("❌ Submission commit FAILED:")
                for err in errors:
                    print(f"  Code: {err.get('code')} - Message: {err.get('details') or err.get('message')}")
                break

        time.sleep(20)

    if state_confirmed:
        print("\n🎉 SUCCESS: Submission accepted by Microsoft Store for certification!")
    else:
        if not state_confirmed:
            print("\n❌ Submission did NOT reach a confirmed state. Possible causes:")
            print("  - Package validation may have failed (check Partner Center)")
            print("  - Microsoft backend may still be processing (check in 15-30 min)")
        sys.exit(1)

if __name__ == '__main__':
    main()
