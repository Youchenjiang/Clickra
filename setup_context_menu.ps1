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
    $sourceDir = $PSScriptRoot
    $sourceExe = Join-Path $sourceDir "ContextTools.exe"
    if (-not (Test-Path $sourceExe)) {
        Write-Host "找不到 ContextTools.exe。請先編譯專案。" -ForegroundColor Red
        return
    }

    # 1. 詢問安裝路徑
    $defaultDir = Join-Path $env:LOCALAPPDATA "ContextTools"
    Write-Host "【安裝位置設定】" -ForegroundColor Cyan
    Write-Host "預設路徑: $defaultDir"
    $customDir = Read-Host "直接按 Enter 使用預設路徑，或輸入自訂安裝路徑"
    $installDir = if ([string]::IsNullOrWhiteSpace($customDir)) { $defaultDir } else { $customDir.Trim().Trim('"') }

    Write-Host "安裝至: $installDir" -ForegroundColor Cyan
    if (-not (Test-Path $installDir)) { New-Item -Path $installDir -ItemType Directory -Force | Out-Null }

    # 2. 編譯 Shell Extension (NativeAOT)
    Write-Host "`n正在編譯 Windows 11 新式選單組件 (NativeAOT)..." -ForegroundColor Cyan
    $shellProj = Join-Path $sourceDir "ContextToolsShell\ContextToolsShell.csproj"
    dotnet publish $shellProj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -p:PublishAot=true --output "$installDir\temp_build" | Out-Null
    
    $shellDll = Join-Path $installDir "ContextToolsShell.dll"
    Copy-Item "$installDir\temp_build\ContextToolsShell.dll" $shellDll -Force
    Remove-Item "$installDir\temp_build" -Recurse -Force

    # 3. 簽署 DLL (新式選單必須簽署)
    Write-Host "正在為組件進行數位簽署 (必要程序)..." -ForegroundColor Cyan
    $certSubject = "CN=ContextTools"
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
    if ($null -eq $cert) {
        Write-Host "建立開發者自簽名憑證..."
        $cert = New-SelfSignedCertificate -Subject $certSubject -Type Custom -KeySpec Signature -SubjectKeyIdentifier $certSubject -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") -CertStoreLocation Cert:\CurrentUser\My
        # 信任此憑證 (需要權限，但開發者模式下通常可略過或自動處理)
        Export-Certificate -Cert $cert -FilePath "$installDir\ContextTools.cer" | Out-Null
        Import-Certificate -FilePath "$installDir\ContextTools.cer" -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    }
    Set-AuthenticodeSignature -FilePath $shellDll -Certificate $cert | Out-Null

    # 4. 複製主程式與檔案
    Copy-Item $sourceExe "$installDir\ContextTools.exe" -Force
    Copy-Item (Join-Path $sourceDir "app.ico") "$installDir\app.ico" -Force
    Copy-Item (Join-Path $sourceDir "AppxManifest.xml") "$installDir\AppxManifest.xml" -Force

    # 5. 註冊 Windows 11 稀疏封裝 (Sparse Package)
    Write-Host "正在註冊 Windows 11 新式選單 (第一層)..." -ForegroundColor Cyan
    try {
        # 先嘗試卸載舊的以更新
        Get-AppxPackage -Name "ContextToolsSparsePackage" | Remove-AppxPackage -ErrorAction SilentlyContinue
        Add-AppxPackage -Path "$installDir\AppxManifest.xml" -Register -ExternalLocation $installDir
    } catch {
        Write-Host "新式選單註冊失敗: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "請確認是否已開啟「開發者模式」。" -ForegroundColor Yellow
    }

    # 6. 建立備用 SendTo 捷徑 (傳統選單)
    $sendToPath = [Environment]::GetFolderPath("SendTo")
    $wshell = New-Object -ComObject WScript.Shell
    $exePath = "$installDir\ContextTools.exe"
    $icoPath = "$installDir\app.ico"
    
    $shortcuts = @(
        @{ Name = "簡報轉 PDF.lnk"; Args = "ppt2pdf" },
        @{ Name = "PDF 合併.lnk"; Args = "merge-pdf" },
        @{ Name = "圖片合併成 PDF.lnk"; Args = "img2pdf" },
        @{ Name = "圖片垂直拼接.lnk"; Args = "img-stitch" }
    )
    foreach ($item in $shortcuts) {
        $s = $wshell.CreateShortcut((Join-Path $sendToPath $item.Name))
        $s.TargetPath = $exePath; $s.Arguments = $item.Args; $s.IconLocation = "$icoPath,0"; $s.Save()
    }

    Write-Host "`n✅ 安裝與註冊成功！" -ForegroundColor Green
    Write-Host "現在您可以直接右鍵點擊檔案，看到「ContextTools (⚡)」選單項目。"
}

function Uninstall-ContextTools {
    Write-Host "【正在移除 ContextTools...】" -ForegroundColor Yellow
    
    # 1. 解除註冊新式選單
    Get-AppxPackage -Name "ContextToolsSparsePackage" | Remove-AppxPackage -ErrorAction SilentlyContinue 
    Write-Host "已移除 Windows 11 新式選單註冊。"

    # 2. 移除 SendTo 捷徑
    $sendToPath = [Environment]::GetFolderPath("SendTo")
    $shortcuts = @("簡報轉 PDF.lnk", "PDF 合併.lnk", "圖片合併成 PDF.lnk", "圖片垂直拼接.lnk")
    foreach ($name in $shortcuts) {
        $path = Join-Path $sendToPath $name
        if (Test-Path $path) { Remove-Item $path -Force }
    }

    # 3. 移除安裝目錄
    $installDir = Join-Path $env:LOCALAPPDATA "ContextTools"
    if (Test-Path $installDir) {
        $confirm = Read-Host "是否要完全刪除安裝目錄 ($installDir)? (y/n)"
        if ($confirm -eq "y") { Remove-Item $installDir -Recurse -Force }
    }

    Write-Host "`n✅ 恢復原狀完成！" -ForegroundColor Green
}

# --- 主程式 ---
Show-Header
Write-Host "1. 安裝 / 更新工具 (包含 Win11 第一層選單)"
Write-Host "2. 移除工具 (恢復原狀)"
Write-Host "3. 退出"
$choice = Read-Host "`n請選擇操作 (1-3)"

switch ($choice) {
    "1" { Install-ContextTools }
    "2" { Uninstall-ContextTools }
    "3" { Exit }
}

Write-Host ""
Read-Host -Prompt "按 Enter 鍵結束..."
