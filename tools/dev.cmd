@echo off
rem Runs dev.sh with the sh of Git for Windows, which is not on the PATH of cmd or PowerShell: usage as dev.sh.
setlocal
for %%G in (git.exe) do set "GIT=%%~$PATH:G"
if not defined GIT (
  echo dev: git is not on the PATH ^(Git for Windows is needed to run the scripts^) >&2
  exit /b 1
)
rem git.exe is in Git\cmd or Git\mingw64\bin; sh.exe is in Git\bin or Git\usr\bin.
for %%R in ("%GIT%\..\.." "%GIT%\..\..\..") do (
  for %%S in ("%%~fR\bin\sh.exe" "%%~fR\usr\bin\sh.exe") do (
    if not defined SH if exist "%%~S" set "SH=%%~S"
  )
)
if not defined SH (
  echo dev: no sh.exe found next to %GIT% >&2
  exit /b 1
)
"%SH%" "%~dp0dev.sh" %*
exit /b %errorlevel%
