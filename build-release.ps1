param(
    [string]$Configuration = "Release",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DotNetArgs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "MandelbrotGpu\MandelbrotGpu.csproj"
$outputPath = Join-Path $repoRoot "bin"

Write-Host "Publishing $Configuration build to $outputPath"

dotnet publish $projectPath -c $Configuration -o $outputPath --self-contained false @DotNetArgs

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host "Release output is available in $outputPath"
