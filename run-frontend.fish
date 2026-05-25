#!/usr/bin/env fish

set -gx DOTNET_ROOT $HOME/.dotnet
fish_add_path -pm $HOME/.dotnet

set project_dir (status dirname)/(dirname (status filename))
cd $project_dir

echo "Deteniendo frontend en puerto 5126..."
fuser -k 5126/tcp 2>/dev/null

echo "Compilando..."
dotnet build Frontend/Frontend.csproj
or exit 1

echo "Iniciando en http://localhost:5126 ..."
dotnet run --project Frontend/Frontend.csproj --launch-profile http
