#requires -RunAsAdministrator
# Stops and removes the WismoApi Windows Service.

$ErrorActionPreference = 'Continue'
sc.exe stop   WismoApi
Start-Sleep -Seconds 2
sc.exe delete WismoApi
