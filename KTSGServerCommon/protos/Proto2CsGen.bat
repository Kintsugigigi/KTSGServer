@echo off
setlocal

:: =======================================================
:: 1. 强制切换到当前 Bat 文件所在的目录
:: 确保 C# 能够扫描到当前文件夹下的 .proto 文件
:: =======================================================
cd /d %~dp0

echo ==========================================
echo      Proto 消息元数据自动生成工具
echo ==========================================

:: ---------------- 配置区域 ----------------

:: [工具名称] 请确保 exe 文件就在当前目录下
set ExeName=Proto2CsGen.exe

:: [参数1] 生成的 MsgMeta.cs 的命名空间
set ProtoNS="KTSG.Proto"

:: [参数2] 生成的元数据文件名
set OutFile="MsgMeta.cs"

:: [参数3] 生成的 Handler 的命名空间 (新加的!)
set HandlerNS="KTSG.NetWork"

:: ------------------------------------------

:: 检查工具是否存在
if not exist "%ExeName%" (
    echo.
    echo [错误] 找不到 "%ExeName%"
    echo quite
    echo 请确认编译后的 exe 文件放在: %~dp0
    echo.
    pause
    exit /b
)

echo [工作目录] %CD%
echo [Handler命名空间] %HandlerNS%
echo.

:: 调用生成器，传递 3 个参数
"%ExeName%" %ProtoNS% %OutFile% %HandlerNS%

echo.
if %ERRORLEVEL% EQU 0 (
    echo [OK] 生成成功！
) else (
    echo [ERROR] 生成失败，请检查上方红字报错。
)

pause