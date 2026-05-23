#requires -RunAsAdministrator
# Installs the WismoApi Windows Service. Run from an elevated PowerShell.

$ErrorActionPreference = 'Stop'

$serviceName = 'WismoApi'
$binPath     = 'C:\wismo\app\Wismo.Api.exe'
$displayName = 'WISMO AI API'
$description = 'WISMO AI multi-tenant support automation API.'

if (-not (Test-Path $binPath)) {
    throw "Binary not found at $binPath. Re-run dotnet publish first."
}

$existing = sc.exe query $serviceName 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "Service $serviceName already exists. Stopping and deleting it first."
    sc.exe stop   $serviceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

sc.exe create $serviceName binPath= "`"$binPath`"" start= auto DisplayName= "$displayName"
sc.exe description $serviceName "$description"
sc.exe failure     $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000

Write-Host "Starting $serviceName..."
sc.exe start $serviceName

Start-Sleep -Seconds 3
sc.exe query $serviceName
