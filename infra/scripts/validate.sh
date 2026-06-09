#!/usr/bin/env bash
# infra/scripts/validate.sh — run only when ADOMCP_VALIDATE_RG is set
# and `az account show` succeeds. Otherwise emit "SKIP" and exit 0.
set -euo pipefail

if [[ -z "${ADOMCP_VALIDATE_RG:-}" ]]; then
  echo "SKIP: ADOMCP_VALIDATE_RG not set; skipping az deployment group validate."
  exit 0
fi

if ! az account show >/dev/null 2>&1; then
  echo "SKIP: no az login; skipping az deployment group validate."
  exit 0
fi

ENV_NAME="${ADOMCP_VALIDATE_ENV:-dev}"
PARAM_FILE="infra/main.${ENV_NAME}.bicepparam"

echo "Validating infra/main.bicep against RG=${ADOMCP_VALIDATE_RG} env=${ENV_NAME}"
az deployment group validate \
  --resource-group "${ADOMCP_VALIDATE_RG}" \
  --template-file infra/main.bicep \
  --parameters "${PARAM_FILE}" \
  --output table
