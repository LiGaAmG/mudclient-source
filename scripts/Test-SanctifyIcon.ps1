$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $root 'Adan.Client.Plugins.GroupWidget'
$resourcesPath = Join-Path $pluginRoot 'Resources.xaml'
$projectPath = Join-Path $pluginRoot 'Adan.Client.Plugins.GroupWidget.csproj'
$iconPath = Join-Path $pluginRoot 'Images/sanctify.png'

if ((Get-Content $resourcesPath -Raw) -notmatch 'x:Key="Brush_Sanctify" ImageSource="Images/sanctify.png"') {
    throw 'Sanctify must use Images/sanctify.png.'
}

if ((Get-Content $projectPath -Raw) -notmatch 'Images\\sanctify.png') {
    throw 'sanctify.png must be included as a project resource.'
}

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'Sanctify icon is missing.'
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($iconPath)
try {
    if ($image.Width -ne 64 -or $image.Height -ne 64) {
        throw "Expected 64x64 icon, got $($image.Width)x$($image.Height)."
    }
}
finally {
    $image.Dispose()
}

if ((Get-FileHash -Algorithm SHA256 $iconPath).Hash -ne '9D7D906B352DCAF1F05248FF95C40998E9EBE32878F009F725B64FEC2320B366') {
    throw 'Sanctify must use its original icon.'
}

Write-Host 'Sanctify icon test passed.'
