# Install with dotnet tool with dotnet tool install --global dotnet-t4

# Get current script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$includePath = Resolve-Path (Join-Path $scriptDir "../../../bin/DebugL/net9.0")
Write-Host "Including path: $includePath"

# Get root path of nuget global packages folder - for windows, macOS and linux
$userProfile = $env:USERPROFILE -replace '\\', '/'
if ($env:USERPROFILE -eq $null) {
    $userProfile = $env:HOME
}

$nugetGlobalPackages = Join-Path $userProfile ".nuget/packages"
Write-Host "NuGet global packages path: $nugetGlobalPackages"


t4 -P="$includePath" -P="$nugetGlobalPackages/system.codedom/9.0.9/lib/net9.0" ./MessageCodes.tt
Write-Host "Generated MessageCodes.cs"