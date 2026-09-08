@if /I "%~1"=="build" goto :build
@if /I "%~1"=="single" goto :single

:usage
   @echo USAGE:
   @echo     rebuild [build] [single]
   @echo.
   @echo ARGUMENTS
   @echo    build - rebuild project with loose files
   @echo    single - rebuild project with stand-alone executable
   @echo.
   @echo    Either build or single are required
   @goto :eof

:build
   rmdir /s /q bin 2>nul
   rmdir /s /q obj 2>nul
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   @goto :eof

:single
   rmdir /s /q bin 2>nul
   rmdir /s /q obj 2>nul
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
   copy README.md bin\Release\net8.0-windows\win-x64\publish
   copy CHANGELOG.md bin\Release\net8.0-windows\win-x64\publish
   @echo You still need to zip up the publish folder
   @goto :eof

