# ----------------------------------------------
# Build script
# ----------------------------------------------

param
(
    [switch] $Release,
    [switch] $ExcludeTests,
    [switch] $ExcludeSamples,
    [switch] $Pack,
    [switch] $Run,
    [switch] $ClearOnly
)

# ----------------------------------------------
# Main
# ----------------------------------------------

$ErrorActionPreference = "Stop"

Import-module "$PSScriptRoot/.psscripts/build-functions.ps1" -Force

Write-BuildHeader "Starting Giraffe.Swagger build script"

if ($ClearOnly.IsPresent)
{
    Remove-OldBuildArtifacts
    return
}

$lib   = "./src/Giraffe.Swagger/Giraffe.Swagger.fsproj"
$tests = "./tests/Giraffe.Swagger.Tests/Giraffe.Swagger.Tests.fsproj"

$version = Get-ProjectVersion $lib
Update-AppVeyorBuildVersion $version

if (Test-IsAppVeyorBuildTriggeredByGitTag)
{
    $gitTag = Get-AppVeyorGitTag
    Test-CompareVersions $version $gitTag
}

Write-DotnetCoreVersions

Remove-OldBuildArtifacts

$configuration = if ($Release.IsPresent) { "Release" } else { "Debug" }

Write-Host "Building Giraffe.Swagger..." -ForegroundColor Magenta
dotnet-build   $lib "-c $configuration"

if (!$ExcludeTests.IsPresent -and !$Run.IsPresent)
{
    Write-Host "Building and running tests..." -ForegroundColor Magenta

    dotnet-build $tests
    dotnet-test  $tests
}

if (!$ExcludeSamples.IsPresent -and !$Run.IsPresent)
{
    Write-Host "Building and testing samples..." -ForegroundColor Magenta

    dotnet-build   $sampleApp

    dotnet-build   $sampleAppTests
    dotnet-test    $sampleAppTests
}

if ($Run.IsPresent)
{
    Write-Host "Launching sample application..." -ForegroundColor Magenta
    dotnet-build   $sampleApp
    dotnet-run     $sampleApp
}

if ($Pack.IsPresent)
{
    Write-Host "Packaging Giraffe.Swagger NuGet package..." -ForegroundColor Magenta

    dotnet-pack $lib "-c $configuration"
}

Write-SuccessFooter "Giraffe.Swagger build completed successfully!"