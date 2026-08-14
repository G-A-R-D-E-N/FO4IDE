@echo off
REM Builds a FRAMEWORK-DEPENDENT FO4RecordEditor as a folder of files.
REM
REM Requires the .NET 9 Desktop Runtime installed on the machine:
REM   https://dotnet.microsoft.com/download/dotnet/9.0  (".NET Desktop Runtime 9.x", x64)
REM
REM Why framework-dependent + folder (not self-contained single-file):
REM   Mod Organizer 2's virtual file system (usvfs) hooks file access. A self-contained .NET
REM   host probes/loads its bundled runtime from the app folder, and usvfs interferes with that
REM   so the process dies before managed code runs (the app "launches" and instantly closes with
REM   no log). A framework-dependent app loads the runtime from the shared install instead, which
REM   usvfs handles, and a plain folder has nothing to self-extract.
REM
REM In MO2, add the FO4RecordEditor.exe inside the publish folder as an executable.

REM Also refresh the plain Release build output (bin\Release\net9.0-windows\FO4RecordEditor.exe).
REM Some launch points use that exe directly; publish only writes the win-x64 subfolder, so without
REM this the plain exe goes stale and "relaunching" runs old code.
dotnet build FO4RecordEditor\FO4RecordEditor.csproj -c Release -p:DebugType=none
if errorlevel 1 ( echo BUILD FAILED. & exit /b 1 )

dotnet publish FO4RecordEditor\FO4RecordEditor.csproj ^
  -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=false ^
  -p:DebugType=none

if errorlevel 1 (
  echo.
  echo ============================================================
  echo BUILD FAILED -- the exe was NOT produced. Scroll up for the error.
  echo   A common cause: a running FO4RecordEditor.exe locking the output.
  echo   Close it ^(or: taskkill /IM FO4RecordEditor.exe /F^) and re-run.
  echo ============================================================
  exit /b 1
)

REM Deploy the fresh build into the MO2 Tools folder the editor is actually launched from,
REM so re-publishing always updates the running launch point (not just the bin\ output).
set "DEPLOY=E:\Modlists\Fallen World Alpha 2\Tools\FO4Editor"
if exist "%DEPLOY%\" (
  echo Deploying to MO2 Tools folder: %DEPLOY%
  robocopy "FO4RecordEditor\bin\Release\net9.0-windows\win-x64\publish" "%DEPLOY%" /MIR /NJH /NJS /NDL /NC /NS /NP >nul
)

echo.
echo ============================================================
echo Published to folder (needs .NET 9 Desktop Runtime installed):
echo   FO4RecordEditor\bin\Release\net9.0-windows\win-x64\publish\
echo Launch the editor from MO2 (Tools\FO4Editor\FO4RecordEditor.exe) -- it was updated above.
echo ============================================================
