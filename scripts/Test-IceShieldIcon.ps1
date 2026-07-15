$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $projectRoot 'Adan.Client.Plugins.GroupWidget'
$resources = Get-Content -Raw (Join-Path $pluginRoot 'Resources.xaml')
$project = Get-Content -Raw (Join-Path $pluginRoot 'Adan.Client.Plugins.GroupWidget.csproj')
$iconPath = Join-Path $pluginRoot 'Images\ice-shield.png'

if ($resources -notmatch 'x:Key="Brush_IceShieldPhoto"[\s\S]*ImageSource="Images/ice-shield.png"' -or $resources -notmatch 'x:Key="Drawing_IceShield"[\s\S]*Brush="\{StaticResource Brush_IceShieldPhoto\}"') {
    throw 'IceShield does not use the raster icon.'
}
if ($project -notmatch '<Resource Include="Images\\ice-shield.png"') {
    throw 'ice-shield.png is not packaged as a resource.'
}
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'ice-shield.png is missing.'
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($iconPath)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) { throw "ice-shield.png must be 64x64; got $($image.Width)x$($image.Height)." }
}
finally { $image.Dispose() }
