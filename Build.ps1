[CmdletBinding()]
param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = $PSScriptRoot
}
$framework = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The .NET Framework 4.x compiler was not found.'
}

$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($output) | Out-Null
$executable = Join-Path $output 'LabwareToSTL.exe'

$arguments = @('/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/target:winexe', '/platform:x86', "/out:$executable")
foreach ($assembly in @('System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xaml.dll')) {
    $arguments += "/reference:$assembly"
}
foreach ($assembly in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'WindowsFormsIntegration.dll')) {
    $arguments += '/reference:' + (Join-Path $framework "WPF\$assembly")
}
$sources = @('Program.cs', 'LabwareExport.cs', 'Assimp.cs', 'Stl.cs', 'ModelViewer.cs', 'PreviewViewport.cs', 'StlMesh.cs', 'DirectXModel.cs')
foreach ($source in $sources) {
    $arguments += Join-Path $PSScriptRoot $source
}

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}
Write-Output "Built $executable"
