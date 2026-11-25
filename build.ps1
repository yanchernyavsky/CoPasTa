$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$project = Join-Path $root 'CoPasTa.csproj'

function Publish-Build {
    param(
        [string]$SelfContained,
        [string]$OutputDir,
        [string]$Label
    )

    Write-Host "Building $Label..."
    $dotnetArgs = @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', $SelfContained,
        '-p:PublishSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $OutputDir
    )

    if ($SelfContained -eq 'true') {
        $dotnetArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'
        $dotnetArgs += '-p:EnableCompressionInSingleFile=true'
    }

    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Label"
    }

    Write-Host "Done: $(Join-Path $OutputDir 'CoPasTa.exe')"
}

Publish-Build 'false' (Join-Path $root 'publish\win-x64') 'framework-dependent'
Publish-Build 'true' (Join-Path $root 'publish\win-x64-portable') 'self-contained portable'
