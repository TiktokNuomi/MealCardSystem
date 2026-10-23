@echo off
rem ============================================================
rem  MealCardSystem launcher
rem
rem  Builds the program if needed, then starts it.
rem  Pass a database path to keep the data somewhere else:
rem      run.bat --db=D:\shared\mealcard.db
rem ============================================================

setlocal
cd /d "%~dp0"

if not exist "bin\MealCardSystem.exe" (
    echo First run: building the program ...
    call build.bat release
    if errorlevel 1 (
        echo.
        echo Build failed, cannot start.
        pause
        exit /b 1
    )
)

start "" "bin\MealCardSystem.exe" %*
exit /b 0
