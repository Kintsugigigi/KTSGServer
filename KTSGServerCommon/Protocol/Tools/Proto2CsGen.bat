@echo off
setlocal

set "ToolDir=%~dp0"
set "ProtocolDir=%ToolDir%.."
set "SchemaDir=%ProtocolDir%\Schema"
set "GeneratedDir=%ProtocolDir%\Generated"
set "HandlerDir=%ProtocolDir%\Handlers"
set "ExePath=%ToolDir%Proto2CsGen.exe"
set "ProtocPath=D:\Tools\protoc-33.2-win64\bin\protoc.exe"
set "ProtoNS=KTSG.Proto"
set "OutFile=..\Generated\MsgMeta.cs"
set "HandlerNS=KTSG.NetWork"

echo ==========================================
echo Proto message generator
echo ==========================================
echo.

if not exist "%SchemaDir%" (
    echo [ERROR] Missing schema dir: "%SchemaDir%"
    pause
    exit /b 1
)

if not exist "%ProtocPath%" (
    echo [ERROR] Missing protoc: "%ProtocPath%"
    pause
    exit /b 1
)

if not exist "%GeneratedDir%" mkdir "%GeneratedDir%"
if not exist "%HandlerDir%" mkdir "%HandlerDir%"

echo [INFO] Running protoc C# generation...
"%ProtocPath%" -I="%SchemaDir%" --csharp_out="%GeneratedDir%" "%SchemaDir%\ktsg_server.proto"
if errorlevel 1 (
    echo [ERROR] Protoc generation failed.
    pause
    exit /b 1
)
echo [INFO] Protoc generation done.
echo.

if not exist "%ExePath%" (
    echo [ERROR] Missing generator: "%ExePath%"
    pause
    exit /b 1
)

pushd "%SchemaDir%" || (
    echo [ERROR] Cannot enter schema dir: "%SchemaDir%"
    pause
    exit /b 1
)

echo [INFO] Working dir: %CD%
echo [INFO] Handler namespace: %HandlerNS%
echo.

"%ExePath%" "%ProtoNS%" "%OutFile%" "%HandlerNS%"
set "GenExitCode=%ERRORLEVEL%"

if exist "%CD%\GenHandlers" (
    move /Y "%CD%\GenHandlers\*.cs" "%HandlerDir%\" >nul
    rmdir "%CD%\GenHandlers"
)

popd

echo.
if "%GenExitCode%"=="0" (
    echo [OK] Generation succeeded.
) else (
    echo [ERROR] Generation failed. See above output.
)

pause
exit /b %GenExitCode%
