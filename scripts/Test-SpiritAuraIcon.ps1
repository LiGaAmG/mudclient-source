$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $projectRoot 'Adan.Client.Plugins.GroupWidget'
$resources = Get-Content -Raw (Join-Path $pluginRoot 'Resources.xaml')
$project = Get-Content -Raw (Join-Path $pluginRoot 'Adan.Client.Plugins.GroupWidget.csproj')
$iconPath = Join-Path $pluginRoot 'Images\spirit-aura.png'

if ($resources -notmatch 'x:Key="Brush_SpiritAura" ImageSource="Images/spirit-aura.png"') {
    throw 'SpiritAura brush does not use the raster icon.'
}
if ($project -notmatch '<Resource Include="Images\\spirit-aura.png"') {
    throw 'spirit-aura.png is not packaged as a resource.'
}
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'spirit-aura.png is missing.'
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($iconPath)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) {
        throw "spirit-aura.png must be 64x64; got $($image.Width)x$($image.Height)."
    }
}
finally {
    $image.Dispose()
}
