# Builds the layer + editor and copies everything into .\dist
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
& "$root\layer\build.bat"
if ($LASTEXITCODE -ne 0) { throw "layer build failed" }
dotnet publish "$root\editor\VRHandMenusEditor.csproj" -c Release -o "$root\editor\bin\publish" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "editor build failed" }
$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$root\layer\build\VRHandMenus.dll" $dist -Force
Copy-Item "$root\package\*" $dist -Force
Copy-Item "$root\editor\bin\publish\*" $dist -Force -Recurse
Write-Host "dist ready: $dist"
