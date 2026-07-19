$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $root 'Adan.Client.Plugins.GroupWidget'
$constants = Get-Content (Join-Path $pluginRoot 'Constants.cs') -Raw
$resources = Get-Content (Join-Path $pluginRoot 'Resources.xaml') -Raw
$project = Get-Content (Join-Path $pluginRoot 'Adan.Client.Plugins.GroupWidget.csproj') -Raw
$iconPath = Join-Path $pluginRoot 'Images\medium-shock-elixir.png'

if ($constants -notmatch 'new AffectDescription\("MediumShockElixir",') {
    throw 'MediumShockElixir affect description is missing.'
}

if ($constants -notmatch '"\[[^\]]+\] [^\"]+"') {
    throw 'MediumShockElixir must match the magic-marked server affect name.'
}

if ($constants -match 'new AffectDescription\("MediumShockElixir",[^\r\n]+\{ IsRoundBased = true \}') {
    throw 'MediumShockElixir is duration-based and must not be marked round-based.'
}

if ($resources -notmatch 'x:Key="Brush_MediumShockElixir" ImageSource="Images/medium-shock-elixir.png"' -or
    $resources -notmatch 'x:Key="Drawing_MediumShockElixir"\s+Brush="\{StaticResource Brush_MediumShockElixir\}"' -or
    $resources -notmatch 'x:Key="ImageSource_MediumShockElixir"\s+Drawing="\{StaticResource Drawing_MediumShockElixir\}"' -or
    $resources -notmatch 'When="MediumShockElixir"\s+Then="\{StaticResource ImageSource_MediumShockElixir\}"') {
    throw 'MediumShockElixir icon mapping is incomplete.'
}

if ($project -notmatch '<Resource Include="Images\\medium-shock-elixir.png"') {
    throw 'medium-shock-elixir.png is not packaged as a resource.'
}

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'MediumShockElixir icon is missing.'
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($iconPath)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) {
        throw "medium-shock-elixir.png must be 64x64; got $($image.Width)x$($image.Height)."
    }
}
finally {
    $image.Dispose()
}

Write-Host 'MediumShockElixir effect test passed.'
