param ($configuration = "Release", $frameworkVersion = "net9.0")

# Old package (MMXMLDoc2Markdown) and the renamed one both provide the 'mmxmldoc2md' command;
# remove the old one first so the update below doesn't conflict on a reused build agent.
# Only uninstall when it is actually present: an unconditional 'dotnet tool uninstall' exits 1
# and writes to stderr when the tool is missing, which Windows PowerShell turns into a
# terminating error under the pipeline's ErrorActionPreference=Stop ('2>$null' does not suppress
# this for native commands) - failing the step on every agent that no longer has the old package.
if (dotnet tool list --global | Select-String -Quiet 'mmxmldoc2markdown') {
    dotnet tool uninstall --global MMXMLDoc2Markdown
}
dotnet tool update --global Meshmakers.XMLDoc2Markdown

$modulePath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$baseBinPath = Join-Path $modulePath "../bin/$configuration/$frameworkVersion"
if (-not (Test-Path -Path $baseBinPath)) {
    throw "Bin path '$baseBinPath' does not exist"
}

$baseOutputPath = Join-Path $baseBinPath "documentation"

# Clean directory
if (Test-Path -Path $baseOutputPath) {
    Write-Host "Remove existing documentation at '$baseOutputPath'"
    Remove-Item -Path $baseOutputPath -Recurse -Force
}

# Create XML documentation for Libraries
$outputPath = "$baseOutputPath/apiReference/ConstructionKit.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.ConstructionKit.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath

$outputPath = "$baseOutputPath/apiReference/Runtime.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.Runtime.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath