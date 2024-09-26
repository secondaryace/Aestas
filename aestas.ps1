#!/usr/bin/pwsh
param (
    [ArgumentCompletions('build', 'test', 'prepare', 'run', 'clean')]
    [string]
    $target
)
$prof = if ($args.Contains('--debug')) { 'Debug' } else { 'Release' }
$nocore = $args.Contains('--nocore')
if ($target -eq 'build')
{
    dotnet fsi prepare.fsx $args
    if (!$nocore -and $LASTEXITCODE -eq 0) { dotnet build ./Aestas.Core.fsproj --configuration $prof }
    if ($LASTEXITCODE -eq 0) { dotnet build ./aestas.fsproj --configuration $prof }
}
elseif ($target -eq 'test')
{
    dotnet fsi prepare.fsx $args
    if (!$nocore -and $LASTEXITCODE -eq 0) { dotnet build ./Aestas.Core.fsproj --configuration Debug }
    if ($LASTEXITCODE -eq 0) { dotnet run --project ./aestas.fsproj }
}
elseif ($target -eq 'prepare')
{
    dotnet fsi prepare.fsx $args
}
elseif ($target -eq 'run') {
    dotnet bin/Release/net8.0/aestas.dll
}
elseif ($target -eq 'clean') {
    dotnet clean ./aestas.fsproj
}
else {
    Write-Output "Usage: aestas.ps1 [build|test|fsproj|run|clean] [--nocli] [--nocore]"
}