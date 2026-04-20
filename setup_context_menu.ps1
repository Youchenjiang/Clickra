# setup_context_menu.ps1
$ErrorActionPreference = "Stop"

$exePath = Resolve-Path ".\ContextTools.exe" -ErrorAction Stop | Select-Object -ExpandProperty Path
if (-not $exePath) {
    Write-Host "ContextTools.exe could not be found. Please ensure it is compiled and present in the same directory." -ForegroundColor Red
    Exit
}

Write-Host "Registering Context Menu commands for ContextTools.exe at: $exePath" -ForegroundColor Cyan

# 1. PPTX to PDF
Write-Host "Registering PPTX to PDF..."
$pptKey = "HKCU:\Software\Classes\SystemFileAssociations\.pptx\shell\ContextTools_PPT2PDF"
New-Item -Path $pptKey -Force | Out-Null
Set-ItemProperty -Path $pptKey -Name "MUIVerb" -Value "⚡ 轉為 PDF (極速)"
Set-ItemProperty -Path $pptKey -Name "Icon" -Value "$exePath"
$pptCmd = "$pptKey\command"
New-Item -Path $pptCmd -Force | Out-Null
Set-ItemProperty -Path $pptCmd -Name "(default)" -Value "`"$exePath`" ppt2pdf `"%1`""

# For Merge operations (Multiple files), we use Windows "SendTo" folder because right-clicking multiple files 
# will spawn N instances of the application instead of passing them all as arguments to one application.
$sendToPath = [Environment]::GetFolderPath("SendTo")

# 2. Merge PDF Shortcut
Write-Host "Registering PDF Merge in SendTo..."
$wshell = New-Object -ComObject WScript.Shell
$pdfShortcut = $wshell.CreateShortcut((Join-Path $sendToPath "⚡ 合併選取的檔案 (PDF).lnk"))
$pdfShortcut.TargetPath = $exePath
$pdfShortcut.Arguments = "merge-pdf"
$pdfShortcut.IconLocation = $exePath
$pdfShortcut.Save()

# 3. Image to PDF Shortcut
Write-Host "Registering Image to PDF in SendTo..."
$imgPdfShortcut = $wshell.CreateShortcut((Join-Path $sendToPath "⚡ 將選取圖片合併為一份 PDF.lnk"))
$imgPdfShortcut.TargetPath = $exePath
$imgPdfShortcut.Arguments = "img2pdf"
$imgPdfShortcut.IconLocation = $exePath
$imgPdfShortcut.Save()

# 4. Image Stitch Shortcut
Write-Host "Registering Image Stitch in SendTo..."
$imgStitchShortcut = $wshell.CreateShortcut((Join-Path $sendToPath "⚡ 將選取圖片垂直拼貼成長圖.lnk"))
$imgStitchShortcut.TargetPath = $exePath
$imgStitchShortcut.Arguments = "img-stitch"
$imgStitchShortcut.IconLocation = $exePath
$imgStitchShortcut.Save()

Write-Host "`nSuccessfully Registered!" -ForegroundColor Green
Write-Host "Usage Tips:"
Write-Host "- For .pptx files: Just Right-Click -> '⚡ 轉為 PDF (極速)'"
Write-Host "- For merging multiple files: Select all files -> Right-Click -> 'Send to (傳送到)' -> Choose the corresponding ⚡ tool."

Read-Host -Prompt "Press Enter to exit"
