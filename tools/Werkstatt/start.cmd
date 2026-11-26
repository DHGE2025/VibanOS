@echo off
rem VibanOS-Werkstatt starten: im Explorer doppelklicken oder im Terminal aufrufen.
rem Oeffnet http://127.0.0.1:5099 im Browser. Beenden: Strg+C oder das Fenster schliessen.
cd /d "%~dp0"
dotnet run -- %*
if errorlevel 1 pause
