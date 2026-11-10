# Registers the OpenXR Hand Menus OpenXR layer (needs admin).
$ErrorActionPreference = 'Stop'
$json = Join-Path $PSScriptRoot 'XR_APILAYER_NOVENDOR_openxr_hand_menus.json'
$key = 'HKLM:\SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit'
if (-not (Test-Path $key)) { New-Item $key -Force | Out-Null }
(Get-Item $key).Property | Where-Object { ($_ -like '*XR_APILAYER_NOVENDOR_openxr_hand_menus.json' -or $_ -like '*XR_APILAYER_NOVENDOR_vr_radial_overlay.json') } |
    ForEach-Object { Remove-ItemProperty $key -Name $_ }
New-ItemProperty $key -Name $json -PropertyType DWord -Value 0 -Force | Out-Null
Write-Host "Installed: $json"
Start-Sleep 1
