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
$icoPath = Join-Path $installDir "app.ico"

Write-Host "正在將常駐執行檔安裝至: $exePath" -ForegroundColor Cyan
Copy-Item -Path $sourceExe -Destination $exePath -Force

# 同時複製 ico 檔至安裝目錄供捷徑直接引用
$sourceIco = Join-Path $PSScriptRoot "app.ico"
if (Test-Path $sourceIco) {
    Copy-Item -Path $sourceIco -Destination $icoPath -Force
} else {
    $icoPath = $exePath  # fallback to exe if ico not found
}

# 2. 清除舊版可能遺留在登錄檔的 PPTX 右鍵選項
$oldPptKey = "HKCU:\Software\Classes\SystemFileAssociations\.pptx\shell\ContextTools_PPT2PDF"
if (Test-Path $oldPptKey) {
    Write-Host "清除舊版右鍵登錄檔殘留..." -ForegroundColor Yellow
    Remove-Item -Path $oldPptKey -Recurse -Force
}

# 3. 清除舊版 SendTo 捷徑 (重新命名前的殘留)
$sendToPath = [Environment]::GetFolderPath("SendTo")
$oldShortcuts = @(
    "批次轉為 PDF (多份PPT).lnk",
    "圖片轉 PDF (合併).lnk",
    "圖片垂直拼貼 (長圖).lnk",
    "合併為單一 PDF.lnk"
)
foreach ($old in $oldShortcuts) {
    $oldPath = Join-Path $sendToPath $old
    if (Test-Path $oldPath) {
        Remove-Item $oldPath -Force
        Write-Host "已移除舊捷徑: $old" -ForegroundColor Yellow
    }
}

# 4. 建立新版 SendTo 捷徑
$wshell = New-Object -ComObject WScript.Shell

Write-Host "Registering 簡報轉 PDF..."
$s = $wshell.CreateShortcut((Join-Path $sendToPath "簡報轉 PDF.lnk"))
$s.TargetPath = $exePath; $s.Arguments = "ppt2pdf"; $s.IconLocation = "$icoPath,0"; $s.Save()

Write-Host "Registering PDF 合併..."
$s = $wshell.CreateShortcut((Join-Path $sendToPath "PDF 合併.lnk"))
$s.TargetPath = $exePath; $s.Arguments = "merge-pdf"; $s.IconLocation = "$icoPath,0"; $s.Save()

Write-Host "Registering 圖片合併成 PDF..."
$s = $wshell.CreateShortcut((Join-Path $sendToPath "圖片合併成 PDF.lnk"))
$s.TargetPath = $exePath; $s.Arguments = "img2pdf"; $s.IconLocation = "$icoPath,0"; $s.Save()

Write-Host "Registering 圖片垂直拼接..."
$s = $wshell.CreateShortcut((Join-Path $sendToPath "圖片垂直拼接.lnk"))
$s.TargetPath = $exePath; $s.Arguments = "img-stitch"; $s.IconLocation = "$icoPath,0"; $s.Save()

Write-Host "`n安裝與註冊成功！" -ForegroundColor Green
Write-Host "執行檔已複製至 AppData。現在您可以隨意刪除下載的原始腳本或資料夾了。"
Write-Host "`n【操作說明】"
Write-Host "- 所有操作：選取一或多個檔案 -> 右鍵 ->「傳送到 (Send to)」 -> 選擇對應的功能"

Read-Host -Prompt "按 Enter 鍵退出..."
