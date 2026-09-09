@if /I "%~1"=="build" goto :build
@if /I "%~1"=="single" goto :single
@if /I "%~1"=="setup" goto :setup

:usage
   @echo USAGE:
   @echo     rebuild [build] [single] [setup]
   @echo.
   @echo ARGUMENTS
   @echo    build  - rebuild project with loose files
   @echo    single - rebuild project with stand-alone executable
   @echo    setup  - rebuild the installer
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
   @goto :eof

:setup
	if exist Output rd /s /q Output
	iscc /Q PrettyReMark.iss
   zip Output\PrettyReMarkV1.07.setup.zip Output\PrettyReMarkV1.07.setup.exe
   @goto :eof

