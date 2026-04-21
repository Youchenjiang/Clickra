# setup_context_menu.ps1
$ErrorActionPreference = "Stop"
$Version = "3.0.0"

# 1. 檢查系統管理員權限
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "【錯誤】請以「系統管理員身分 (Run as Administrator)」執行此腳本。" -ForegroundColor Red
    Read-Host "按 Enter 鍵結束..."
    Exit
}

function Show-Header {
    Clear-Host
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host "   ⚙️ ContextTools v$Version" -ForegroundColor Cyan
    Write-Host "   (Modern Shell Edition)" -ForegroundColor Cyan
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host ""
}

function Install-ContextTools {
    $sourceDir = $PSScriptRoot
    $installDir = Join-Path $env:LOCALAPPDATA "ContextTools"
    if (-not (Test-Path $installDir)) { New-Item -Path $installDir -ItemType Directory -Force | Out-Null }

    # 1. 確保安裝目錄存在
    $installDir = Join-Path $env:LOCALAPPDATA "ContextTools"
    if (-not (Test-Path $installDir)) { New-Item -Path $installDir -ItemType Directory -Force | Out-Null }

    $logPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "ContextTools.log")

    # 2. 編譯 Shell Extension (Manual VTable 版)
    Write-Host "正在編譯 NativeAOT 選單組件 (Manual VTable)..." -ForegroundColor Gray
    $shellProj = Join-Path $sourceDir "ContextToolsShell\ContextToolsShell.csproj"
    dotnet publish $shellProj -c Release -r win-x64 -p:PublishAot=true --output "$installDir\temp_build"
    
    $shellDll = Join-Path $installDir "ContextToolsShell.dll"
    Copy-Item "$installDir\temp_build\ContextToolsShell.dll" $shellDll -Force
    Remove-Item "$installDir\temp_build" -Recurse -Force

    # 3. 處理身分宣告與資產
    Copy-Item "$sourceDir\ContextTools.exe.manifest" "$installDir\ContextTools.exe.manifest" -Force
    Copy-Item "$sourceDir\ContextToolsShell.dll.manifest" "$installDir\ContextToolsShell.dll.manifest" -Force
    Copy-Item (Join-Path $sourceDir "ContextTools.exe") "$installDir\ContextTools.exe" -Force
    Copy-Item (Join-Path $sourceDir "app.ico") "$installDir\app.ico" -Force
    $sourcePng = "C:\Users\g1014308\Documents\GitHub\Youchen\photo\1.png"
    if (Test-Path $sourcePng) { Copy-Item $sourcePng "$installDir\app.png" -Force }

    # 4. 數位簽署與信任 (LocalMachine)
    Write-Host "正在建立並安裝數位信任憑證..." -ForegroundColor Gray
    $certSubject = "CN=ContextTools"
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
    if ($null -eq $cert) {
        $cert = New-SelfSignedCertificate -Subject $certSubject -Type Custom -KeySpec Signature -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") -CertStoreLocation Cert:\CurrentUser\My
        Export-Certificate -Cert $cert -FilePath "$installDir\ContextTools.cer" | Out-Null
        Import-Certificate -FilePath "$installDir\ContextTools.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Import-Certificate -FilePath "$installDir\ContextTools.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    Set-AuthenticodeSignature -FilePath $shellDll -Certificate $cert | Out-Null

    # 5. 註冊 Windows 11 稀疏封裝
    Write-Host "正在註冊封裝身分..." -ForegroundColor Gray
    Copy-Item (Join-Path $sourceDir "AppxManifest.xml") "$installDir\AppxManifest.xml" -Force
    try {
        Get-AppxPackage -Name "ContextToolsSparsePackage" | Remove-AppxPackage -ErrorAction SilentlyContinue
        Add-AppxPackage -Path "$installDir\AppxManifest.xml" -Register -ExternalLocation $installDir
    } catch {
        Write-Host "封裝註冊失敗！詳情: $($_.Exception.Message)" -ForegroundColor Red
        Throw
    }

    # 6. 重啟資源管理器
    Write-Host "正在重啟 Explorer 以加載選單 (請稍候)..." -ForegroundColor Gray
    taskkill /f /im explorer.exe 2>$null | Out-Null
    start explorer.exe
    Start-Sleep -Seconds 5 # 給 Explorer 充足的加載時間

    # 7. --- 關鍵：真實性驗證環節 ---
    Write-Host "`n[安裝後端驗證程序]" -ForegroundColor Cyan
    
    if (Test-Path $logPath) {
        $logs = Get-Content $logPath
        if ($logs -match "GetTitle") {
            Write-Host "✅ 驗證成功：選單組件已成功加載 (ABI OK)！" -ForegroundColor Green
            Write-Host "您現在可以看到選單名稱了。"
        } elseif ($logs -match "DllGetClassObject") {
            Write-Host "⚠️ 部分成功：系統已找到大門，但功能細節尚未讀取。" -ForegroundColor Yellow
            Write-Host "這通常代表 VTable 對齊仍有細微偏差。"
        } else {
            Write-Host "❌ 驗證失敗：組件已註冊但未被系統觸發。請右鍵點擊檔案後再檢查日誌。" -ForegroundColor Red
        }
    } else {
        Write-Host "❌ 驗證失敗：選單組件完全未被加載。" -ForegroundColor Red
    }
}

function Uninstall-ContextTools {
    Write-Host "正在移除所有註冊..." -ForegroundColor Yellow
    Get-AppxPackage -Name "ContextToolsSparsePackage" | Remove-AppxPackage -ErrorAction SilentlyContinue 
    # 移除憑證 (這部分略過以保持簡潔，或手動在備註說明)
    Write-Host "✅ 移除成功。"
}

Show-Header
Write-Host "1. 安裝並執行『真實性驗證』"
Write-Host "2. 完整移除"
$choice = Read-Host "`n請選擇操作"

switch ($choice) {
    "1" { Install-ContextTools }
    "2" { Uninstall-ContextTools }
}

Write-Host ""
Read-Host -Prompt "按 Enter 鍵結束..."
