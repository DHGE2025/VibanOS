#!/bin/zsh
# VibanOS-Werkstatt starten: im Finder doppelklicken oder im Terminal aufrufen.
# Öffnet http://127.0.0.1:5099 im Browser. Beenden: Ctrl+C oder das Terminalfenster schließen.
cd "${0:A:h}" || exit 1

# dotnet@10 aus Homebrew ist keg-only: Ohne DOTNET_ROOT finden dotnet und cosmos ihre Laufzeit nicht.
if [[ -z "$DOTNET_ROOT" ]]; then
  for d in /opt/homebrew/opt/dotnet@10/libexec /usr/local/opt/dotnet@10/libexec \
           /opt/homebrew/opt/dotnet/libexec /usr/local/opt/dotnet/libexec /usr/local/share/dotnet; do
    [[ -x "$d/dotnet" ]] && { export DOTNET_ROOT="$d"; break; }
  done
fi
[[ -n "$DOTNET_ROOT" ]] && export PATH="$DOTNET_ROOT:$PATH"
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH:$HOME/.dotnet/tools"

# Endet die Werkstatt mit Code 42, hat "Aktualisieren" sie selbst geändert: neu bauen und wieder starten.
# Die geschweiften Klammern lassen zsh alles vorab lesen, falls ein Update auch diese Datei ändert.
{
  export WERKSTATT_STARTER=1
  while true; do
    dotnet run -- "$@"
    code=$?
    [[ $code == 42 ]] || break
    export WERKSTATT_NEUSTART=1
  done
  if [[ $code != 0 ]]; then
    echo; echo "Die Werkstatt ist mit einem Fehler beendet (siehe oben)."; read -k1 "?Taste drücken zum Schließen "
  fi
  exit $code
}
