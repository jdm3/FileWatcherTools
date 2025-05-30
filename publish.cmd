@echo off
if "%~1"=="" goto usage
if not exist %~s1\NUL goto usage
goto run
:usage
    echo usage: publish.cmd path_to_bin_dir
    exit /b 1

:run
    msbuild /nologo /verbosity:minimal /p:configuration=release "%~dp0onchanged\onchanged.sln"
    if not "%errorlevel%"=="0" exit /b 1

    msbuild /nologo /verbosity:minimal /p:configuration=release "%~dp0autobuild\autobuild.sln"
    if not "%errorlevel%"=="0" exit /b 1

    robocopy "%~dp0onchanged\build\release\bin" %1 onchanged.exe onchangedlib.dll /NJH /NJS
    if %errorlevel% GEQ 8 exit /b 1

    robocopy "%~dp0autobuild\build\release\bin" %1 autobuild.exe /NJH /NJS
    if %errorlevel% GEQ 8 exit /b 1

    echo.
    echo done

