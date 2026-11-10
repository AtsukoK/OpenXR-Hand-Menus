# Removes the VR Radial Overlay OpenXR layer registration (needs admin).
$key = 'HKLM:\SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit'
if (Test-Path $key) {
    (Get-Item $key).Property | Where-Object { $_ -like '*XR_APILAYER_NOVENDOR_vr_radial_overlay.json' } |
        ForEach-Object { Remove-ItemProperty $key -Name $_; Write-Host "Removed: $_" }
}
Start-Sleep 1
