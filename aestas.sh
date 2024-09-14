#!/bin/bash
target="$1"

if [ "$target" = "build" ]; then
    dotnet fsi prepare.fsx
    dotnet build --configuration Release
elif [ "$target" = "test" ]; then
    dotnet fsi prepare.fsx
    dotnet run
elif [ "$target" = "prepare" ]; then
    dotnet fsi prepare.fsx
elif [ "$target" = "run" ]; then
    dotnet bin/Release/net8.0/aestas.dll
elif [ "$target" = "clean" ]; then
    dotnet clean
else
    echo "Usage: aestas.sh [build|test|fsproj|run|clean]"
fi
