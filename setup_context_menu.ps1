# setup_context_menu.ps1
$ErrorActionPreference = "Stop"
$AppName = "Clickra"
$Version = "3.0.2.0"

# 0. 自動提升權限 (Auto-Elevation)
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "正在嘗試以系統管理員身分重新啟動腳本..." -ForegroundColor Cyan
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    Exit
}

function Show-Header {
    Clear-Host
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host "      $AppName v$Version" -ForegroundColor Cyan
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host ""
}

function Get-InstallDir {
    $defaultDir = Join-Path $env:LOCALAPPDATA $AppName
    Write-Host "預設安裝路徑: $defaultDir" -ForegroundColor Gray
    $inputDir = Read-Host "請輸入安裝路徑 (直接按 Enter 使用預設)"
    if ([string]::IsNullOrWhiteSpace($inputDir)) { return $defaultDir }
    return $inputDir
}

function Smart-Copy {
    param([string]$Source, [string]$Destination)
    try {
        Copy-Item $Source $Destination -Force -ErrorAction Stop
    } catch {
        if ($_.Exception.Message -match "being used" -or $_.Exception.Message -match "使用中" -or $_.CategoryInfo.Category -eq "ResourceUnavailable") {
            $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
            $oldPath = "$Destination.old_$timestamp"
            Write-Host "⚠️ 檔案正在被系統鎖定，正在執行「改名覆蓋法」進行無感更新..." -ForegroundColor Yellow
            try {
                Move-Item $Destination $oldPath -Force -ErrorAction Stop
                Copy-Item $Source $Destination -Force -ErrorAction Stop
                Write-Host "✅ 鎖定解除，更新成功。" -ForegroundColor Green
            } catch {
                Write-Host "❌ 無法自動解除鎖定：$($_.Exception.Message)" -ForegroundColor Red
                throw $_
            }
        } else {
            throw $_
        }
    }
}

function Install-Project {
    # 智慧路徑識別：優先使用腳本目錄，若為空（如互動式執行）則使用當前目錄
    $sourceDir = if ([string]::IsNullOrEmpty($PSScriptRoot)) { Get-Location } else { $PSScriptRoot }
    
    # 驗證執行檔是否存在
    $exePath = Join-Path $sourceDir "$AppName.exe"
    if (-not (Test-Path $exePath)) {
        Write-Host "❌ 找不到 $AppName.exe！" -ForegroundColor Red
        Write-Host "請確保 .ps1 腳本與 .exe 執行檔放在同一個資料夾內。" -ForegroundColor Yellow
        return
    }

    $installDir = Get-InstallDir
    
    if (-not (Test-Path $installDir)) { 
        New-Item -Path $installDir -ItemType Directory -Force | Out-Null 
    }

    # 1. 清理舊的備份檔
    Get-ChildItem $installDir -Filter "*.old_*" | Remove-Item -Force -ErrorAction SilentlyContinue

    $logPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "$AppName.log")

    # 2. 部署核心組件與資產
    Write-Host "正在部署核心組件至: $installDir" -ForegroundColor Gray
    
    # 先拷貝主程式
    Smart-Copy (Join-Path $sourceDir "$AppName.exe") "$installDir\$AppName.exe"
    
    # 執行內置部署引擎
    & "$installDir\$AppName.exe" --deploy "$installDir" | Out-Null
    
    $shellDll = Join-Path $installDir "$AppName`Shell.dll"

    # 4. 數位簽署與信任
    Write-Host "正在確保數位信任憑證與開發者權限..." -ForegroundColor Gray
    
    # 智慧繞過：強制開啟側載與開發者安裝原則
    $unlockPath = "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
    reg add $unlockPath /v AllowAllTrustedApps /t REG_DWORD /d 1 /f | Out-Null
    reg add $unlockPath /v AllowDevelopmentWithoutDevLicense /t REG_DWORD /d 1 /f | Out-Null

    $certSubject = "CN=$AppName"
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
    if ($null -eq $cert) {
        $cert = New-SelfSignedCertificate -Subject $certSubject -Type Custom -KeySpec Signature -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") -CertStoreLocation Cert:\CurrentUser\My
        Export-Certificate -Cert $cert -FilePath "$installDir\$AppName.cer" | Out-Null
        Import-Certificate -FilePath "$installDir\$AppName.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Import-Certificate -FilePath "$installDir\$AppName.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    
    # 關鍵步驟：必須同時簽署執行檔與 DLL
    $exePath = Join-Path $installDir "$AppName.exe"
    Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert | Out-Null
    Set-AuthenticodeSignature -FilePath $shellDll -Certificate $cert | Out-Null

    # 5. 註冊 Windows 11 稀疏封裝 (僅支援 Win11 Build 22000+)
    $isWin11 = [Environment]::OSVersion.Version.Build -ge 22000
    if ($isWin11) {
        Write-Host "偵測到 Windows 11，正在註冊現代化選單封裝..." -ForegroundColor Gray
        try {
            # 徹底靜音舊封裝移除
            $oldPkg = Get-AppxPackage -Name "$AppName`SparsePackage" -ErrorAction SilentlyContinue
            if ($null -ne $oldPkg) {
                $oldPkg | Remove-AppxPackage -ErrorAction SilentlyContinue | Out-Null
            }
            
            # 智慧版本同步：將 AppxManifest 的版本與執行檔同步
            $exeFile = Get-Item (Join-Path $installDir "$AppName.exe")
            $exeVersion = $exeFile.VersionInfo.FileVersion
            if ($exeVersion -match "^\d+\.\d+\.\d+$") { $exeVersion += ".0" } 
            
            $manifestPath = Join-Path $installDir "AppxManifest.xml"
            [xml]$manifest = Get-Content $manifestPath -Encoding UTF8
            $manifest.Package.Identity.Version = $exeVersion
            $manifest.Save($manifestPath)
            
            Add-AppxPackage -Path "$manifestPath" -Register -ExternalLocation $installDir -ErrorAction Stop | Out-Null
            Write-Host "✅ 現代選單註冊成功 (版本: $exeVersion)" -ForegroundColor Green
        } catch {
            Write-Host "⚠️ 現代選單註冊失敗，但主程式已部署。可能原因：開發人員模式未完全開啟。 詳情: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "ℹ️ 偵測到 Windows 10 或更舊版本。" -ForegroundColor Yellow
        Write-Host "提示：Windows 11 之前的系統不支援現代化子選單。此工具目前僅針對 Windows 11 優化。" -ForegroundColor Gray
    }

    # 7. 完成提示
    Write-Host "`n🎉 $AppName 安裝成功！" -ForegroundColor Green
    Write-Host "請嘗試對 PPT、PDF 或圖片檔案點擊右鍵，即可看到 $AppName 選單。" -ForegroundColor Cyan
    Write-Host "提示：若未立即出現，可能需要重啟檔案總管或稍候數秒。" -ForegroundColor Gray

    # 8. 安全性清理詢問
    Write-Host "`n為了安裝，我們暫時啟動了開發者安裝原則。" -ForegroundColor Gray
    $cleanChoice = Read-Host "是否要在完成後恢復系統安全性（關閉開發人員模式繞過）？ [Y/n] (預設 Y)"
    
    # 預設為 Y，且不區分大小寫
    if ([string]::IsNullOrWhiteSpace($cleanChoice) -or $cleanChoice.ToUpper() -eq "Y") {
        Write-Host "正在恢復安全設定..." -ForegroundColor Gray
        reg delete $unlockPath /v AllowDevelopmentWithoutDevLicense /f | Out-Null
        Write-Host "✅ 已恢復系統安全設定。" -ForegroundColor Green
    } else {
        Write-Host "ℹ️ 已跳過安全性恢復，系統仍將保留開發者安裝權限。" -ForegroundColor Yellow
    }
}

function Uninstall-Project {
    $installDir = Get-InstallDir
    Write-Host "正在移除此工具的所有註冊..." -ForegroundColor Yellow
    Get-AppxPackage -Name "$AppName`SparsePackage" | Remove-AppxPackage -ErrorAction SilentlyContinue 
    
    # 自動清理資料夾
    if (Test-Path $installDir) {
        Write-Host "正在清理安裝資料夾..." -ForegroundColor Gray
        try {
            Remove-Item $installDir -Recurse -Force -ErrorAction Stop
            Write-Host "✅ 環境清理完成。" -ForegroundColor Green
        } catch {
            $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
            Move-Item $installDir "$installDir.deleted_$timestamp" -Force -ErrorAction SilentlyContinue
            Write-Host "⚠️ 部分檔案被鎖定，已將資料夾標記為待刪除。" -ForegroundColor Yellow
        }
    }
}

Show-Header
Write-Host "1. 安裝 / 更新工具 (自動配置)"
Write-Host "2. 移除工具 (自動清理)"
$choice = Read-Host "`n請選擇操作"

switch ($choice) {
    "1" { Install-Project }
    "2" { Uninstall-Project }
}

Write-Host ""
Read-Host -Prompt "操作完成。按 Enter 鍵結束..."
