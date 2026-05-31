param([Parameter(Mandatory=$true)][string]$Version)
Write-Host "Use clients workspace release script:"
Write-Host "powershell -ExecutionPolicy Bypass -File C:\bot\repos\adan-refactor-clients-workspace\scripts\release.ps1 -Version $Version"
