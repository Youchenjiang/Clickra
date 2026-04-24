# setup_context_menu.ps1
$ErrorActionPreference = "Stop"
$AppName = "Clickra"
$Version = "3.0.0.1"

# 0. 自動提升權限 (Auto-Elevation) - 修復引號解析問題
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "正在嘗試以系統管理員身分重新啟動腳本..." -ForegroundColor Cyan
    $args = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell.exe -ArgumentList $args -Verb RunAs
    Exit
}

function Show-Header {
    Clear-Host
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host "      ${AppName} v${Version}" -ForegroundColor Cyan
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
        if (Test-Path $Destination) {
            Move-Item $Destination "$Destination.old" -Force -ErrorAction SilentlyContinue
        }
        Copy-Item $Source $Destination -Force -ErrorAction Stop
    } catch {
        Write-Host "警告: 檔案 $Destination 可能正在使用中，將嘗試在下次重啟後更新。" -ForegroundColor Yellow
        Copy-Item $Source $Destination -Force -ErrorAction SilentlyContinue
    }
}

function Install-Project {
    $sourceDir = if ([string]::IsNullOrEmpty($PSScriptRoot)) { Get-Location } else { $PSScriptRoot }
    $exeSource = Join-Path $sourceDir "${AppName}.exe"
    
    if (-not (Test-Path $exeSource)) {
        Write-Host "❌ 找不到 ${AppName}.exe！" -ForegroundColor Red
        return
    }

    $installDir = Get-InstallDir
    if (-not (Test-Path $installDir)) { New-Item -Path $installDir -ItemType Directory -Force | Out-Null }

    Write-Host "正在部署組件至: $installDir" -ForegroundColor Gray
    
    # 1. 拷貝主程式與執行佈署引擎
    Smart-Copy $exeSource "$installDir\${AppName}.exe"
    & "$installDir\${AppName}.exe" --deploy "$installDir" | Out-Null
    
    # 2. 強制開啟側載權限 (修復 0x80073CFF 錯誤)
    Write-Host "正在配置系統側載權限..." -ForegroundColor Gray
    $unlockPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
    if (-not (Test-Path $unlockPath)) { New-Item $unlockPath -Force | Out-Null }
    Set-ItemProperty -Path $unlockPath -Name "AllowAllTrustedApps" -Value 1 -Type DWord -Force
    Set-ItemProperty -Path $unlockPath -Name "AllowDevelopmentWithoutDevLicense" -Value 1 -Type DWord -Force

    $shellDll = Join-Path $installDir "${AppName}Shell.dll"

    # 2. 憑證與簽署
    Write-Host "正在配置數位簽署..." -ForegroundColor Gray
    $certSubject = "CN=${AppName}"
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
    if ($null -eq $cert) {
        $cert = New-SelfSignedCertificate -Subject $certSubject -Type Custom -KeySpec Signature -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") -CertStoreLocation Cert:\CurrentUser\My
        Export-Certificate -Cert $cert -FilePath "$installDir\${AppName}.cer" | Out-Null
        Import-Certificate -FilePath "$installDir\${AppName}.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Import-Certificate -FilePath "$installDir\${AppName}.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    
    Set-AuthenticodeSignature -FilePath "$installDir\${AppName}.exe" -Certificate $cert | Out-Null
    Set-AuthenticodeSignature -FilePath $shellDll -Certificate $cert | Out-Null

    # 3. 註冊封裝
    Write-Host "正在註冊 Windows 11 選單封裝..." -ForegroundColor Gray
    try {
        $packageName = "${AppName}SparsePackage"
        Get-AppxPackage -Name $packageName | Remove-AppxPackage -ErrorAction SilentlyContinue
        
        $manifestPath = Join-Path $installDir "AppxManifest.xml"
        Add-AppxPackage -Path $manifestPath -Register -ExternalLocation $installDir
    } catch {
        Write-Host "註冊失敗: $($_.Exception.Message)" -ForegroundColor Red
        return
    }

    Write-Host "`n🎉 ${AppName} 安裝成功！" -ForegroundColor Green
    Write-Host "提示：若選單未出現，請重新啟動檔案總管。" -ForegroundColor Gray
}

function Uninstall-Project {
    $installDir = Get-InstallDir
    $packageName = "${AppName}SparsePackage"
    Write-Host "正在移除註冊..." -ForegroundColor Yellow
    Get-AppxPackage -Name $packageName | Remove-AppxPackage -ErrorAction SilentlyContinue 
    
    if (Test-Path $installDir) {
        Write-Host "正在清理資料夾..." -ForegroundColor Gray
        Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "✅ 已移除。" -ForegroundColor Green
}

Show-Header
Write-Host "1. 安裝 / 更新"
Write-Host "2. 移除"
$choice = Read-Host "`n請選擇 [1/2]"

if ($choice -eq "1") { Install-Project }
elseif ($choice -eq "2") { Uninstall-Project }

Write-Host ""
Read-Host -Prompt "按 Enter 結束..."
