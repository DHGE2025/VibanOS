@echo off
rem VibanOS-Werkstatt starten: im Explorer doppelklicken oder im Terminal aufrufen.
rem Oeffnet http://127.0.0.1:5099 im Browser. Beenden: Strg+C oder das Fenster schliessen.
cd /d "%~dp0"
rem Endet die Werkstatt mit Code 42, hat "Aktualisieren" sie selbst geaendert: neu bauen und wieder starten.
set WERKSTATT_STARTER=1
:start
dotnet run -- %*
if %errorlevel%==42 (
  set WERKSTATT_NEUSTART=1
  goto start
)
if errorlevel 1 pause
