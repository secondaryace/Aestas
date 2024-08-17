#!/usr/bin/pwsh
param (
    [string]$target
)
if ($target -eq 'build')
{
    dotnet fsi prepare.fsx
    dotnet build --configuration Release
}
elseif ($target -eq 'test')
{
    dotnet fsi prepare.fsx
    dotnet run
}
elseif ($target -eq 'prepare')
{
    dotnet fsi prepare.fsx
}
elseif ($target -eq 'run') {
    dotnet bin/Release/net8.0/aestas.dll
}
elseif ($target -eq 'clean') {
    dotnet clean
}
else {
    Write-Output "Usage: aestas.ps1 [build|test|fsproj|run|clean]"
}