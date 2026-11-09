#!/usr/bin/env bash
set -euo pipefail

: "${AI__APIKEY:?Falta AI__APIKEY. Exportala con: export AI__APIKEY='tu-clave-de-opencode'}"
MODEL="${AI__MODEL:-gemini-4-argon}"

if [[ "${1:-}" == "--list-models" ]]; then
  curl -sS -H "Authorization: Bearer ${AI__APIKEY}" \
    "https://opencode.ai/inference/v1/models"
  exit 0
fi

echo "Probando OpenCode con el modelo '${MODEL}'..."
curl -sS -w '\nHTTP %{http_code}\n' \
  -H "Authorization: Bearer ${AI__APIKEY}" \
  -H 'Content-Type: application/json' \
  -d '{"contents":[{"parts":[{"text":"Respondé solamente: OK"}]}]}' \
  "https://opencode.ai/inference/google/v1beta/models/${MODEL}:generateContent"
