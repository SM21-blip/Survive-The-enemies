@echo off
setlocal
rundll32.exe "%~dp0VirtualFolderApplet.cpl",RunUtility
if errorlevel 1 (
  echo Virtual Folder Utility did not start.
  pause
)
