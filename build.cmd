@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

echo ========================================
echo  locmodel_patch 编译脚本
echo ========================================
echo 编译器: %CSC%

if not exist "%CSC%" (
    echo.
    echo [错误] 未找到 .NET Framework 4.x 编译器 csc.exe
    echo        请确认本机已启用 .NET Framework 4.x（Windows 10/11 默认自带）
    pause
    exit /b 1
)

if not exist "locmodel_patch.cs" (
    echo.
    echo [错误] 当前目录下找不到 locmodel_patch.cs
    pause
    exit /b 1
)

echo.
echo [1/2] 正在编译 locmodel_patch.cs ...
"%CSC%" /nologo /target:exe /codepage:65001 /optimize+ /out:locmodel_patch.exe locmodel_patch.cs

if errorlevel 1 (
    echo.
    echo [2/2] [失败] 编译未通过，请查看上方 csc.exe 输出的错误信息
    pause
    exit /b 1
)

echo.
echo [2/2] [成功] 已生成: %~dp0locmodel_patch.exe
pause
exit /b 0