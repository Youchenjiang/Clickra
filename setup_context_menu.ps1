# setup_context_menu.ps1
$ErrorActionPreference = "Stop"
$Version = "3.0.0"

# 0. 自動提升權限 (Auto-Elevation)
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "正在嘗試以系統管理員身分重新啟動腳本..." -ForegroundColor Cyan
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
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

function Install-ContextTools {
    $sourceDir = $PSScriptRoot
    $installDir = Join-Path $env:LOCALAPPDATA "ContextTools"
    if (-not (Test-Path $installDir)) { New-Item -Path $installDir -ItemType Directory -Force | Out-Null }

    # 1. 清理舊的備份檔
    Get-ChildItem $installDir -Filter "*.old_*" | Remove-Item -Force

    $logPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "ContextTools.log")

    # 2. 部署核心組件與資產 (使用內置部署引擎)
    Write-Host "正在部署核心組件與資產..." -ForegroundColor Gray
    
    # 先拷貝主程式
    Smart-Copy (Join-Path $sourceDir "ContextTools.exe") "$installDir\ContextTools.exe"
    
    # 執行內置部署引擎提取 XML, PNG 與 DLL
    & "$installDir\ContextTools.exe" --deploy "$installDir" | Out-Null
    
    $shellDll = Join-Path $installDir "ContextToolsShell.dll"

    # 4. 數位簽署與信任
    Write-Host "正在確保數位信任憑證..." -ForegroundColor Gray
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
    try {
        Get-AppxPackage -Name "ContextToolsSparsePackage" | Remove-AppxPackage -ErrorAction SilentlyContinue
        Add-AppxPackage -Path "$installDir\AppxManifest.xml" -Register -ExternalLocation $installDir
    } catch {
        Write-Host "封裝註冊失敗！詳情: $($_.Exception.Message)" -ForegroundColor Red
        Throw
    }


    # 7. 驗證
    Write-Host "`n[安裝後端驗證程序]" -ForegroundColor Cyan
    if (Test-Path $logPath) {
        $logs = Get-Content $logPath
        if ($logs -match "GetTitle" -or $logs -match "Invoke") {
            Write-Host "✅ 驗證成功：組件運作正常！" -ForegroundColor Green
        } else {
            Write-Host "❌ 驗證失敗：組件未被系統觸發。" -ForegroundColor Red
        }
    } else {
        Write-Host "❌ 驗證失敗：未找到運行日誌。" -ForegroundColor Red
    }
}

function Uninstall-ContextTools {
    Write-Host "正在移除此工具的所有註冊..." -ForegroundColor Yellow
    Get-AppxPackage -Name "ContextToolsSparsePackage" | Remove-AppxPackage -ErrorAction SilentlyContinue 
    Write-Host "✅ 移除成功。您可以手動刪除 $env:LOCALAPPDATA\ContextTools 資料夾。"
}

Show-Header
Write-Host "1. 極速安裝 (自動解鎖/自動提權)"
Write-Host "2. 完整移除"
$choice = Read-Host "`n請選擇操作"

switch ($choice) {
    "1" { Install-ContextTools }
    "2" { Uninstall-ContextTools }
}

Write-Host ""
Read-Host -Prompt "操作完成。按 Enter 鍵結束..."
