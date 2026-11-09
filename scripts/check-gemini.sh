#!/usr/bin/env bash
set -euo pipefail

: "${AI__APIKEY:?Falta AI__APIKEY. Exportala con: export AI__APIKEY='tu-clave'}"
MODEL="${AI__MODEL:-gemini-3.8-flash}"

if [[ "${1:-}" == "--list-models" ]]; then
  curl -sS "https://generativelanguage.googleapis.com/v1beta/models?key=${AI__APIKEY}"
  exit 0
fi

echo "Probando modelo '${MODEL}'..."
curl -sS -w '\nHTTP %{http_code}\n' \
  -H 'Content-Type: application/json' \
  -d '{"contents":[{"parts":[{"text":"Respondé solamente: OK"}]}]}' \
  "https://generativelanguage.googleapis.com/v1beta/models/${MODEL}:generateContent?key=${AI__APIKEY}"
