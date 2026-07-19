$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $root 'Adan.Client.Plugins.GroupWidget'
$constants = Get-Content (Join-Path $pluginRoot 'Constants.cs') -Raw
$resources = Get-Content (Join-Path $pluginRoot 'Resources.xaml') -Raw
$project = Get-Content (Join-Path $pluginRoot 'Adan.Client.Plugins.GroupWidget.csproj') -Raw
$iconPath = Join-Path $pluginRoot 'Images\holy-rite.png'

if ($constants -notmatch 'new AffectDescription\("HolyRite",') {
    throw 'HolyRite affect description is missing.'
}

if ($resources -notmatch 'x:Key="Brush_HolyRite" ImageSource="Images/holy-rite.png"' -or
    $resources -notmatch 'x:Key="Drawing_HolyRite"\s+Brush="\{StaticResource Brush_HolyRite\}"' -or
    $resources -notmatch 'x:Key="ImageSource_HolyRite"\s+Drawing="\{StaticResource Drawing_HolyRite\}"' -or
    $resources -notmatch 'When="HolyRite"\s+Then="\{StaticResource ImageSource_HolyRite\}"') {
    throw 'HolyRite icon mapping is incomplete.'
}

if ($project -notmatch '<Resource Include="Images\\holy-rite.png"') {
    throw 'holy-rite.png is not packaged as a resource.'
}

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'HolyRite icon is missing.'
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($iconPath)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) {
        throw "holy-rite.png must be 64x64; got $($image.Width)x$($image.Height)."
    }
}
finally {
    $image.Dispose()
}

Write-Host 'HolyRite effect test passed.'
