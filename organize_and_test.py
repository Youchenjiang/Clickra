import os
import shutil
import subprocess

# Define paths
workspace_dir = r"C:\Users\g1014308\Documents\GitHub\Youchen"
clickra_dir = os.path.join(workspace_dir, "Clickra")
test_pdfs_dir = os.path.join(clickra_dir, "test_pdfs")
source_dir = os.path.join(test_pdfs_dir, "source")
translated_dir = os.path.join(test_pdfs_dir, "translated")
diagnostic_dir = os.path.join(test_pdfs_dir, "diagnostic")

# Ensure test_pdfs directory exists
for path in (test_pdfs_dir, source_dir, translated_dir, diagnostic_dir):
    os.makedirs(path, exist_ok=True)

# List of source test PDFs
source_pdfs = [
    os.path.join(workspace_dir, "114423046_final_project.pdf"),
    os.path.join(workspace_dir, "2407.11279v1.pdf"),
    os.path.join(workspace_dir, "2407.11279v1_clean.pdf"),
    os.path.join(workspace_dir, "2407.11279v1_test_annot.pdf"),
    os.path.join(workspace_dir, "PentestAgent_Agent Pentest.pdf"),
    os.path.join(workspace_dir, "TOGLL_Oracle Generation.pdf"),
    os.path.join(clickra_dir, "SemTaint.pdf"),
    os.path.join(clickra_dir, "SemTaint_p1.pdf")
]

# 1. Clean up previously generated files in test folders
print("--- Cleaning up previous translated files and logs ---")
paths_to_clean = [test_pdfs_dir, source_dir, translated_dir, diagnostic_dir]
deleted_files_count = 0
for path in paths_to_clean:
    if not os.path.exists(path):
        continue
    for file in os.listdir(path):
        if file.endswith("_translated.pdf") or file.endswith("_renderdbg.log") or file.endswith("_health.json"):
            full_file_path = os.path.join(path, file)
            try:
                os.remove(full_file_path)
                print(f"Deleted: {full_file_path}")
                deleted_files_count += 1
            except Exception as e:
                print(f"Error deleting {full_file_path}: {e}")

print(f"Cleaned up {deleted_files_count} files.")

# 2. Copy source PDFs to test_pdfs folder
print("\n--- Organizing test PDFs ---")
copied_pdfs = []
for src in source_pdfs:
    if os.path.exists(src):
        dst = os.path.join(source_dir, os.path.basename(src))
        try:
            shutil.copy2(src, dst)
            print(f"Copied: {src} -> {dst}")
            copied_pdfs.append(dst)
        except Exception as e:
            print(f"Error copying {src}: {e}")
    else:
        print(f"Source PDF not found: {src}")

# 3. Run translation for each PDF in test_pdfs
print("\n--- Starting Translation of Test PDFs ---")
cli_csproj = os.path.join(clickra_dir, "src", "Clickra.CLI", "Clickra.csproj")

for pdf in copied_pdfs:
    filename = os.path.basename(pdf)
    print(f"\n==================================================")
    print(f"Translating: {filename}")
    print(f"==================================================")
    
    cmd = [
        "dotnet", "run",
        "--project", cli_csproj,
        "--", "translate-pdf", "--quiet", pdf
    ]
    
    try:
        # Run translation process and stream output
        process = subprocess.Popen(
            cmd,
            cwd=clickra_dir,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding='utf-8',
            errors='replace'
        )
        
        for line in process.stdout:
            print(line.strip())
            
        process.wait()
        if process.returncode == 0:
            out_pdf = os.path.join(source_dir, os.path.splitext(filename)[0] + "_translated.pdf")
            out_log = os.path.join(source_dir, os.path.splitext(filename)[0] + "_renderdbg.log")
            if os.path.exists(out_pdf):
                moved_pdf = os.path.join(translated_dir, os.path.basename(out_pdf))
                shutil.move(out_pdf, moved_pdf)
                print(f"Moved translated PDF: {moved_pdf}")
            if os.path.exists(out_log):
                moved_log = os.path.join(diagnostic_dir, os.path.basename(out_log))
                shutil.move(out_log, moved_log)
                print(f"Moved render log: {moved_log}")
            print(f"Success: {filename} translated successfully.")
        else:
            print(f"Failed: {filename} translation process returned code {process.returncode}.")
            
    except Exception as e:
        print(f"Error running translation for {filename}: {e}")

print("\n--- All Done! ---")
