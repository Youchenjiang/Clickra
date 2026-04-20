# setup_context_menu.ps1
$ErrorActionPreference = "Stop"

$sourceExe = Join-Path $PSScriptRoot "ContextTools.exe"
if (-not (Test-Path $sourceExe)) {
    Write-Host "找不到 ContextTools.exe。請確認執行檔與本腳本放在同一個資料夾。" -ForegroundColor Red
    Exit
}

# 1. 將執行檔安裝至系統使用者安全路徑 (AppData\Local)
$installDir = Join-Path $env:LOCALAPPDATA "ContextTools"
if (-not (Test-Path $installDir)) {
    New-Item -Path $installDir -ItemType Directory -Force | Out-Null
}

$exePath = Join-Path $installDir "ContextTools.exe"
Write-Host "正在將常駐執行檔安裝至: $exePath" -ForegroundColor Cyan
Copy-Item -Path $sourceExe -Destination $exePath -Force

# 2. 清除舊版可能遺留在登錄檔的 PPTX 右鍵選項 (現已統一由 SendTo 處理)
$oldPptKey = "HKCU:\Software\Classes\SystemFileAssociations\.pptx\shell\ContextTools_PPT2PDF"
if (Test-Path $oldPptKey) {
    Write-Host "清除舊版右鍵登錄檔殘留..." -ForegroundColor Yellow
    Remove-Item -Path $oldPptKey -Recurse -Force
}

# 3. 所有功能統一透過 SendTo 資料夾掛載
$sendToPath = [Environment]::GetFolderPath("SendTo")
$wshell = New-Object -ComObject WScript.Shell

Write-Host "Registering PPTX to PDF in SendTo..."
$pptShortcut = $wshell.CreateShortcut((Join-Path $sendToPath "批次轉為 PDF (多份PPT).lnk"))
$pptShortcut.TargetPath = $exePath
$pptShortcut.Arguments = "ppt2pdf"
$pptShortcut.IconLocation = $exePath
$pptShortcut.Save()

Write-Host "Registering Merge PDF in SendTo..."
$pdfShortcut = $wshell.CreateShortcut((Join-Path $sendToPath "合併為單一 PDF.lnk"))
$pdfShortcut.TargetPath = $exePath
$pdfShortcut.Arguments = "merge-pdf"
$pdfShortcut.IconLocation = $exePath
$pdfShortcut.Save()

Write-Host "Registering Image to PDF in SendTo..."
$imgPdfShortcut = $wshell.CreateShortcut((Join-Path $sendToPath "圖片轉 PDF (合併).lnk"))
$imgPdfShortcut.TargetPath = $exePath
$imgPdfShortcut.Arguments = "img2pdf"
$imgPdfShortcut.IconLocation = $exePath
$imgPdfShortcut.Save()

Write-Host "Registering Image Stitch in SendTo..."
$imgStitchShortcut = $wshell.CreateShortcut((Join-Path $sendToPath "圖片垂直拼貼 (長圖).lnk"))
$imgStitchShortcut.TargetPath = $exePath
$imgStitchShortcut.Arguments = "img-stitch"
$imgStitchShortcut.IconLocation = $exePath
$imgStitchShortcut.Save()

Write-Host "`n安裝與註冊成功！" -ForegroundColor Green
Write-Host "執行檔已複製至 AppData。現在您可以隨意刪除下載的原始腳本或資料夾了。"
Write-Host "`n【操作說明】"
Write-Host "- 所有操作：選取一或多個檔案 -> 右鍵 ->「傳送到 (Send to)」 -> 選擇對應的功能"

Read-Host -Prompt "按 Enter 鍵退出..."
