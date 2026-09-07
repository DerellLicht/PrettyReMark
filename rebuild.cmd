rmdir /s /q bin 2>nul
rmdir /s /q obj 2>nul
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
