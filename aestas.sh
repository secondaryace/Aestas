#!/bin/bash
target="$1"
prof="Release"
nocore=false

for arg in "$@"; do
    case $arg in
        --debug)
            prof="Debug"
            ;;
        --nocore)
            nocore=true
            ;;
    esac
done

if [ "$target" = "build" ]; then
    dotnet fsi prepare.fsx "$@"
    if [ "$nocore" = false ]; then
        dotnet build ./Aestas.Core.fsproj --configuration "$prof"
    fi
    dotnet build ./aestas.fsproj --configuration "$prof"
elif [ "$target" = "test" ]; then
    dotnet fsi prepare.fsx "$@"
    if [ "$nocore" = false ]; then
        dotnet build ./Aestas.Core.fsproj --configuration Debug
    fi
    dotnet run --project ./aestas.fsproj
elif [ "$target" = "prepare" ]; then
    dotnet fsi prepare.fsx "$@"
elif [ "$target" = "run" ]; then
    dotnet bin/Release/net8.0/aestas.dll
elif [ "$target" = "clean" ]; then
    dotnet clean ./aestas.fsproj
else
    echo "Usage: aestas.sh [build|test|prepare|run|clean] [--debug] [--nocore]"
fi