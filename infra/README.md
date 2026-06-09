# infra/ — Bicep modules for ADO MCP Bridge

Resources defined here implement design spec §6. Names follow
`docs/superpowers/plans/_shared-contracts.md`.

## Local validate

```bash
# Lint
az bicep lint --file infra/main.bicep

# Build (produces infra/main.json next to source)
az bicep build --file infra/main.bicep

# What-if against an existing RG (requires `az login`)
az deployment group what-if \
  --resource-group rg-adomcp-dev \
  --template-file infra/main.bicep \
  --parameters infra/main.dev.bicepparam
```

## PSRule for Azure

```bash
Invoke-PSRule -InputPath infra/main.json -Module PSRule.Rules.Azure -As Detail
```
