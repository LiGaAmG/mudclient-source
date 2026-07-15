$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $projectRoot 'Adan.Client.Plugins.GroupWidget'
$constants = Get-Content -Raw (Join-Path $pluginRoot 'Constants.cs')
$resources = Get-Content -Raw (Join-Path $pluginRoot 'Resources.xaml')
$project = Get-Content -Raw (Join-Path $pluginRoot 'Adan.Client.Plugins.GroupWidget.csproj')
$iconPath = Join-Path $pluginRoot 'Images\fire-shield.png'

if ($constants -notmatch 'new AffectDescription\("FireShield"') {
    throw 'FireShield affect is not registered.'
}
if ($resources -notmatch 'x:Key="Brush_FireShield" ImageSource="Images/fire-shield.png"') {
    throw 'FireShield brush does not use the raster icon.'
}
if ($resources -notmatch 'x:Key="Drawing_FireShield"' -or $resources -notmatch 'x:Key="ImageSource_FireShield"' -or $resources -notmatch 'When="FireShield"') {
    throw 'FireShield icon resource mapping is incomplete.'
}
if ($project -notmatch '<Resource Include="Images\\fire-shield.png"') {
    throw 'fire-shield.png is not packaged as a resource.'
}
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'fire-shield.png is missing.'
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($iconPath)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) {
        throw "fire-shield.png must be 64x64; got $($image.Width)x$($image.Height)."
    }
}
finally {
    $image.Dispose()
}
