# setup_context_menu.ps1
$ErrorActionPreference = "Stop"

function Show-Header {
    Clear-Host
    Write-Host "==================================" -ForegroundColor Magenta
    Write-Host "   ⚡ Windows ContextTools ⚡   " -ForegroundColor Magenta
    Write-Host "==================================" -ForegroundColor Magenta
    Write-Host ""
}

function Install-ContextTools {
    $sourceExe = Join-Path $PSScriptRoot "ContextTools.exe"
    if (-not (Test-Path $sourceExe)) {
        Write-Host "找不到 ContextTools.exe。請確認執行檔與本腳本放在同一個資料夾。" -ForegroundColor Red
        return
    }

    # 1. 詢問安裝路徑
    $defaultDir = Join-Path $env:LOCALAPPDATA "ContextTools"
    Write-Host "【安裝位置設定】" -ForegroundColor Cyan
    Write-Host "預設路徑: $defaultDir"
    $customDir = Read-Host "直接按 Enter 使用預設路徑，或輸入自訂安裝路徑"

    if ([string]::IsNullOrWhiteSpace($customDir)) {
        $installDir = $defaultDir
    } else {
        $installDir = $customDir.Trim().Trim('"')
    }

    Write-Host "安裝至: $installDir" -ForegroundColor Cyan

    if (-not (Test-Path $installDir)) {
        New-Item -Path $installDir -ItemType Directory -Force | Out-Null
    }

    $exePath = Join-Path $installDir "ContextTools.exe"
    $icoPath = Join-Path $installDir "app.ico"

    Copy-Item -Path $sourceExe -Destination $exePath -Force

    # 同時複製 ico 檔至安裝目錄供捷徑直接引用
    $sourceIco = Join-Path $PSScriptRoot "app.ico"
    if (Test-Path $sourceIco) {
        Copy-Item -Path $sourceIco -Destination $icoPath -Force
    } else {
        $icoPath = $exePath
    }

    # 2. 清理與註冊捷徑
    $sendToPath = [Environment]::GetFolderPath("SendTo")
    $wshell = New-Object -ComObject WScript.Shell

    # 先清理舊版名稱
    $oldShortcuts = @("批次轉為 PDF (多份PPT).lnk", "圖片轉 PDF (合併).lnk", "圖片垂直拼貼 (長圖).lnk", "合併為單一 PDF.lnk")
    foreach ($old in $oldShortcuts) {
        $oldPath = Join-Path $sendToPath $old
        if (Test-Path $oldPath) { Remove-Item $oldPath -Force }
    }

    Write-Host "正在建立右鍵選單捷徑..." -ForegroundColor Cyan
    
    $shortcuts = @(
        @{ Name = "簡報轉 PDF.lnk"; Args = "ppt2pdf" },
        @{ Name = "PDF 合併.lnk"; Args = "merge-pdf" },
        @{ Name = "圖片合併成 PDF.lnk"; Args = "img2pdf" },
        @{ Name = "圖片垂直拼接.lnk"; Args = "img-stitch" }
    )

    foreach ($item in $shortcuts) {
        Write-Host "註冊: $($item.Name)"
        $s = $wshell.CreateShortcut((Join-Path $sendToPath $item.Name))
        $s.TargetPath = $exePath
        $s.Arguments = $item.Args
        $s.IconLocation = "$icoPath,0"
        $s.Save()
    }

    # 3. 清理舊版登錄檔
    $oldPptKey = "HKCU:\Software\Classes\SystemFileAssociations\.pptx\shell\ContextTools_PPT2PDF"
    if (Test-Path $oldPptKey) { Remove-Item -Path $oldPptKey -Recurse -Force }

    Write-Host "`n✅ 安裝與註冊成功！" -ForegroundColor Green
    Write-Host "執行檔已複製至 $installDir"
    Write-Host "現在您可以隨意刪除下載的原始腳本資料夾了。"
}

function Uninstall-ContextTools {
    Write-Host "【正在移除 ContextTools...】" -ForegroundColor Yellow
    
    # 1. 移除 SendTo 捷徑
    $sendToPath = [Environment]::GetFolderPath("SendTo")
    $shortcuts = @(
        "簡報轉 PDF.lnk", "PDF 合併.lnk", "圖片合併成 PDF.lnk", "圖片垂直拼接.lnk",
        "批次轉為 PDF (多份PPT).lnk", "圖片轉 PDF (合併).lnk", "圖片垂直拼貼 (長圖).lnk", "合併為單一 PDF.lnk"
    )
    
    $foundInstallDir = $null

    foreach ($name in $shortcuts) {
        $path = Join-Path $sendToPath $name
        if (Test-Path $path) {
            if ($null -eq $foundInstallDir) {
                # 嘗試從捷徑找出安裝目錄
                $wshell = New-Object -ComObject WScript.Shell
                $lnk = $wshell.CreateShortcut($path)
                $foundInstallDir = Split-Path $lnk.TargetPath
            }
            Remove-Item $path -Force
            Write-Host "已移除捷徑: $name"
        }
    }

    # 2. 移除登錄檔
    $oldPptKey = "HKCU:\Software\Classes\SystemFileAssociations\.pptx\shell\ContextTools_PPT2PDF"
    if (Test-Path $oldPptKey) {
        Remove-Item -Path $oldPptKey -Recurse -Force
        Write-Host "已移除右鍵登錄檔項。"
    }

    # 3. 移除安裝目錄
    if ($null -eq $foundInstallDir) {
        $foundInstallDir = Join-Path $env:LOCALAPPDATA "ContextTools"
    }

    if (Test-Path $foundInstallDir) {
        Write-Host ""
        Write-Host "偵測到安裝目錄: $foundInstallDir" -ForegroundColor Cyan
        $confirm = Read-Host "是否要刪除安裝目錄下的所有檔案 (exe/ico)? (y/n)"
        if ($confirm -eq "y") {
            Remove-Item $foundInstallDir -Recurse -Force
            Write-Host "已刪除資料夾與執行檔。" -ForegroundColor Green
        } else {
            Write-Host "已保留資料夾與執行檔。"
        }
    }

    Write-Host "`n✅ 恢復原狀完成！" -ForegroundColor Green
}

# --- 主程式 ---
Show-Header
Write-Host "1. 安裝 / 更新工具"
Write-Host "2. 移除工具 (恢復原狀)"
Write-Host "3. 退出"
Write-Host ""
$choice = Read-Host "請選擇操作 (1-3)"

switch ($choice) {
    "1" { Install-ContextTools }
    "2" { Uninstall-ContextTools }
    "3" { Exit }
    default { Write-Host "無效的選擇。" -ForegroundColor Red }
}

Write-Host ""
Read-Host -Prompt "按 Enter 鍵結束..."
