$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $projectRoot 'Adan.Client.Plugins.GroupWidget'
$resources = Get-Content -Raw (Join-Path $pluginRoot 'Resources.xaml')
$project = Get-Content -Raw (Join-Path $pluginRoot 'Adan.Client.Plugins.GroupWidget.csproj')
$iconPath = Join-Path $pluginRoot 'Images\shock.png'

if ($resources -notmatch 'x:Key="Brush_ShockPhoto"[\s\S]*ImageSource="Images/shock.png"' -or $resources -notmatch 'x:Key="Drawing_Shock"[\s\S]*Brush="\{StaticResource Brush_ShockPhoto\}"') {
    throw 'Shock brush does not use the raster icon.'
}
if ($project -notmatch '<Resource Include="Images\\shock.png"') {
    throw 'shock.png is not packaged as a resource.'
}
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'shock.png is missing.'
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($iconPath)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) {
        throw "shock.png must be 64x64; got $($image.Width)x$($image.Height)."
    }
}
finally {
    $image.Dispose()
}
