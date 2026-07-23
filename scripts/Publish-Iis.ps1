<#
.SYNOPSIS
  IIS icin framework-dependent publish.
  Sunucu: .NET 10 Hosting Bundle (AspNetCoreModuleV2).
#>
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\IisPaket")
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$webOut = Join-Path $OutputRoot "Web"
$apiOut = Join-Path $OutputRoot "Api"

$publishArgs = @(
    "-c", "Release",
    "--self-contained", "false",
    "-p:PublishReadyToRun=false",
    "-p:PublishSingleFile=false"
)

function Clear-PublishFolder([string]$path) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

function Write-IisWebConfig([string]$folder, [string]$dllName) {
    $configPath = Join-Path $folder "web.config"
    # Alt uygulama (fasonapi) parent Web'in aspNetCore handler'ini miras alir;
    # tekrar <add> edilirse IIS 500.19 duplicate hatasi verir → once <remove>.
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <handlers>
      <remove name="aspNetCore" />
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
    </handlers>
    <aspNetCore processPath="dotnet" arguments=".\$dllName" stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" hostingModel="outofprocess">
      <environmentVariables>
        <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
      </environmentVariables>
    </aspNetCore>
  </system.webServer>
</configuration>
"@
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($configPath, $xml.Trim() + "`r`n", $utf8NoBom)

    $logs = Join-Path $folder "logs"
    if (-not (Test-Path $logs)) {
        New-Item -ItemType Directory -Path $logs | Out-Null
    }
}

function Test-PublishOutput([string]$folder, [string]$dllName, [string]$label) {
    $dll = Join-Path $folder $dllName
    $sni = Join-Path $folder "runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll"
    $webConfig = Join-Path $folder "web.config"
    $qz = Join-Path $folder "wwwroot\lib\qz\qz-tray.js"

    $errors = @()
    if (-not (Test-Path $dll)) { $errors += "Eksik: $dllName" }
    if (-not (Test-Path $sni)) { $errors += "Eksik: SqlClient SNI" }
    if (-not (Test-Path $webConfig)) { $errors += "Eksik: web.config" }
    if ($label -eq "Web" -and -not (Test-Path $qz)) { $errors += "Eksik: QZ Tray JS (wwwroot\lib\qz\qz-tray.js)" }

    $configText = Get-Content $webConfig -Raw -ErrorAction SilentlyContinue
    if ($configText -notmatch '<aspNetCore') {
        $errors += "web.config icinde aspNetCore yok"
    }

    Get-ChildItem -Path $folder -Filter "FasonBarkod.*.exe" -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "  Kalinti silindi: $($_.Name)" -ForegroundColor DarkYellow
    }

    if ($errors.Count -gt 0) {
        throw "$label publish dogrulamasi basarisiz:`n  - $($errors -join "`n  - ")"
    }
    Write-Host "  OK $label" -ForegroundColor Green
}

Write-Host "IIS framework-dependent publish..." -ForegroundColor Cyan

Clear-PublishFolder $webOut
Clear-PublishFolder $apiOut

dotnet publish (Join-Path $repoRoot "src\FasonBarkod.Web\FasonBarkod.Web.csproj") @publishArgs -o $webOut
if ($LASTEXITCODE -ne 0) { throw "Web publish failed." }

dotnet publish (Join-Path $repoRoot "src\FasonBarkod.Api\FasonBarkod.Api.csproj") @publishArgs -o $apiOut
if ($LASTEXITCODE -ne 0) { throw "API publish failed." }

Write-IisWebConfig $webOut "FasonBarkod.Web.dll"
Write-IisWebConfig $apiOut "FasonBarkod.Api.dll"

Test-PublishOutput $webOut "FasonBarkod.Web.dll" "Web"
Test-PublishOutput $apiOut "FasonBarkod.Api.dll" "Api"

Write-Host ""
Write-Host "Hazir: $OutputRoot" -ForegroundColor Green
Write-Host "  Web -> IIS site kok dizini"
Write-Host "  Api  -> IIS alt uygulama (alias: fasonapi)"
