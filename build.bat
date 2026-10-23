@echo off
rem ============================================================
rem  MealCardSystem build script
rem
rem  Usage:
rem    build.bat            Build the main application (release)
rem    build.bat debug      Build with debug symbols, no optimize
rem    build.bat selftest   Build and run the hardware-free self test
rem    build.bat uismoke    Build and run the interface smoke test
rem    build.bat check      Build and run both tests
rem    build.bat run        Build then launch the application
rem    build.bat clean      Remove build output
rem
rem  Why csc.exe instead of MSBuild/dotnet:
rem    This machine has no .NET SDK and no NuGet, but the .NET Framework
rem    ships a C# compiler. Calling it directly is the most reliable path.
rem    NOTE: that compiler only supports C# 5 syntax.
rem
rem  Why /platform:x86:
rem    UHFReader18CSharp.dll P/Invokes the native 32-bit Basic.dll.
rem    A 64-bit process would fail with BadImageFormatException.
rem ============================================================

setlocal enabledelayedexpansion

cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] Cannot find csc.exe under %WINDIR%\Microsoft.NET
    echo         Install .NET Framework 4.x and retry.
    exit /b 1
)

if not exist "bin"   mkdir "bin"
if not exist "data"  mkdir "data"

rem ---- shared source files --------------------------------------
set "SRC_CORE=src\Model\Card.cs src\Model\Transaction.cs src\Model\Operator.cs"
set "SRC_CORE=%SRC_CORE% src\Util\Money.cs src\Util\Hex.cs src\Util\Csv.cs"
set "SRC_CORE=%SRC_CORE% src\Data\Sqlite.cs src\Data\Database.cs"
set "SRC_CORE=%SRC_CORE% src\Data\CardRepository.cs src\Data\TransactionRepository.cs src\Data\OperatorRepository.cs"
set "SRC_CORE=%SRC_CORE% src\Reader\RfidReader.cs"
set "SRC_CORE=%SRC_CORE% src\Service\BusinessException.cs src\Service\CardService.cs src\Service\AccountService.cs"
set "SRC_CORE=%SRC_CORE% src\Service\ReportService.cs src\Service\AppSession.cs"

set "SRC_UI=src\Program.cs"
set "SRC_UI=%SRC_UI% src\UI\UiKit.cs src\UI\FunctionPanel.cs src\UI\CardScanBox.cs"
set "SRC_UI=%SRC_UI% src\UI\LoginForm.cs src\UI\MainForm.cs"
set "SRC_UI=%SRC_UI% src\UI\ConsumePanel.cs src\UI\RechargePanel.cs src\UI\IssuePanel.cs"
set "SRC_UI=%SRC_UI% src\UI\QueryPanel.cs src\UI\CardManagePanel.cs src\UI\OperatorPanel.cs"

set "REFS=/noconfig /nostdlib+ /platform:x86 /warn:4"
set "REFS=%REFS% /reference:mscorlib.dll /reference:System.dll /reference:System.Core.dll"
set "REFS=%REFS% /reference:System.Drawing.dll /reference:System.Windows.Forms.dll"
set "REFS=%REFS% /reference:lib\UHFReader18CSharp.dll"

rem ---- argument routing -----------------------------------------
if /i "%~1"=="clean"    goto :clean
if /i "%~1"=="selftest" goto :selftest
if /i "%~1"=="uismoke"  goto :uismoke
if /i "%~1"=="check"    goto :check
if /i "%~1"=="run"      goto :buildrun
if /i "%~1"=="debug"    goto :builddebug
goto :buildrelease

rem ---- release build --------------------------------------------
:buildrelease
echo [1/2] Compiling MealCardSystem.exe (release, x86) ...
"%CSC%" -nologo -target:winexe -optimize+ -out:bin\MealCardSystem.exe %REFS% %SRC_CORE% %SRC_UI%
if errorlevel 1 goto :failed
goto :postbuild

rem ---- debug build ----------------------------------------------
:builddebug
echo [1/2] Compiling MealCardSystem.exe (debug, x86) ...
"%CSC%" -nologo -target:winexe -optimize- -debug:pdbonly -out:bin\MealCardSystem.exe %REFS% %SRC_CORE% %SRC_UI%
if errorlevel 1 goto :failed
goto :postbuild

rem ---- copy runtime dependencies --------------------------------
:postbuild
echo [2/2] Copying reader DLLs next to the executable ...
copy /y "lib\UHFReader18CSharp.dll" "bin\" >nul
if errorlevel 1 goto :copyfailed
copy /y "lib\Basic.dll"             "bin\" >nul
if errorlevel 1 goto :copyfailed

echo.
echo Build succeeded: bin\MealCardSystem.exe
echo Run it with:   run.bat
exit /b 0

rem ---- self test ------------------------------------------------
:selftest
echo [1/3] Compiling SelfTest.exe ...
"%CSC%" -nologo -target:exe -platform:x86 -out:bin\SelfTest.exe %REFS% %SRC_CORE% tests\SelfTest.cs
if errorlevel 1 goto :failed

rem The reader DLLs are copied even though the self test needs no hardware:
rem AppSession constructs an RfidReader, so loading the assembly proves the
rem x86 platform setting and the DLL deployment are both correct.
echo [2/3] Copying reader DLLs next to the executable ...
copy /y "lib\UHFReader18CSharp.dll" "bin\" >nul
if errorlevel 1 goto :copyfailed
copy /y "lib\Basic.dll"             "bin\" >nul
if errorlevel 1 goto :copyfailed

echo [3/3] Running self test ...
echo.
bin\SelfTest.exe
set SELFTEST_RC=%ERRORLEVEL%
echo.
if not "%SELFTEST_RC%"=="0" (
    echo Self test FAILED with exit code %SELFTEST_RC%.
    exit /b %SELFTEST_RC%
)
echo Self test passed.
exit /b 0

rem ---- interface smoke test -------------------------------------
:uismoke
call "%~f0" release
if errorlevel 1 exit /b 1

echo [3/3] Running interface smoke test ...
echo.
bin\MealCardSystem.exe --uismoke
set SMOKE_RC=%ERRORLEVEL%
if "%SMOKE_RC%"=="0" (
    echo Interface smoke test passed.
    exit /b 0
)
echo Interface smoke test FAILED with exit code %SMOKE_RC%.
if exist "%TEMP%\mealcard_uismoke.log" type "%TEMP%\mealcard_uismoke.log"
exit /b %SMOKE_RC%

rem ---- run every test -------------------------------------------
:check
call "%~f0" selftest
if errorlevel 1 exit /b 1
call "%~f0" uismoke
if errorlevel 1 exit /b 1
echo.
echo All checks passed.
exit /b 0

rem ---- build then run -------------------------------------------
:buildrun
call "%~f0" release
if errorlevel 1 exit /b 1
start "" "bin\MealCardSystem.exe"
exit /b 0

rem ---- clean ----------------------------------------------------
:clean
echo Removing build output ...
if exist "bin\MealCardSystem.exe" del /q "bin\MealCardSystem.exe"
if exist "bin\SelfTest.exe"       del /q "bin\SelfTest.exe"
if exist "bin\UHFReader18CSharp.dll" del /q "bin\UHFReader18CSharp.dll"
if exist "bin\Basic.dll"          del /q "bin\Basic.dll"
if exist "bin\*.pdb"              del /q "bin\*.pdb"
echo Done. The data\ folder was left untouched.
exit /b 0

rem ---- errors ---------------------------------------------------
:failed
echo.
echo [ERROR] Compilation failed. See the messages above.
exit /b 1

:copyfailed
echo.
echo [ERROR] Failed to copy reader DLLs into bin\.
echo         Make sure lib\UHFReader18CSharp.dll and lib\Basic.dll exist.
exit /b 1
