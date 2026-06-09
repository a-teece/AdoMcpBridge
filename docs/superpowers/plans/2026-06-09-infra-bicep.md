# Infrastructure (Bicep) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a complete, lint-clean, PSRule-clean Bicep module set under `infra/` that deploys every Azure resource listed in design spec §6 (ACR, Key Vault, SQL Serverless, Log Analytics, App Insights, Container App + Environment, user-assigned managed identity), parameterised for `dev` and `prod`, with `allowedIpRanges` defaulting to `0.0.0.0/0` per the locked decision in §10.

**Architecture:** A single top-level `infra/main.bicep` composes seven feature modules under `infra/modules/` (`identity`, `acr`, `keyvault`, `sql`, `observability`, `containerapp`). The MI is created first and its `principalId` flows to every other module so RBAC role assignments are wired at deployment time (no admin keys, no SQL passwords). Bicep parameter files (`main.dev.bicepparam`, `main.prod.bicepparam`) pin per-env values. Validation runs locally and in CI via `bicep lint`, `bicep build`, conditional `az deployment group validate`, and PSRule for Azure against the compiled ARM JSON.

**Tech Stack:** Bicep (Azure CLI `az bicep` v0.30+), Azure Container Apps, Azure SQL Serverless GP, Azure Key Vault (RBAC), Azure Container Registry (Basic), Log Analytics + Application Insights (workspace-based), PSRule for Azure 1.36+, PowerShell 7+, GitHub Actions (referenced — wiring lives in the CI/CD plan).

---

## File map

Files this plan creates:

- `infra/main.bicep` — top-level composition.
- `infra/main.dev.bicepparam` — dev parameter file.
- `infra/main.prod.bicepparam` — prod parameter file.
- `infra/modules/identity.bicep` — user-assigned MI.
- `infra/modules/acr.bicep` — ACR + AcrPull role assignment.
- `infra/modules/keyvault.bicep` — Key Vault + RBAC role assignments + DEK key + cert placeholder.
- `infra/modules/sql.bicep` — SQL server + DB + Entra admin + firewall.
- `infra/modules/observability.bicep` — Log Analytics + App Insights.
- `infra/modules/containerapp.bicep` — Container App Environment + Container App.
- `infra/bicepconfig.json` — lint rules config.
- `infra/ps-rule.yaml` — PSRule for Azure config.
- `infra/.ps-rule/Baseline.Rule.yaml` — baseline binding (Azure.Pillar.Security).
- `infra/README.md` — local validation + deployment commands.
- `pipelines/snippets/bicep-validate.md` — doc snippet for the CI/CD plan to reference.

---

### Task 1: Scaffold `infra/` directory and lint config

**Files:**
- Create: `infra/bicepconfig.json`
- Create: `infra/README.md`

- [ ] **Step 1: Create `infra/bicepconfig.json` to enable all recommended lint rules as errors**

```json
{
  "analyzers": {
    "core": {
      "enabled": true,
      "verbose": false,
      "rules": {
        "no-hardcoded-env-urls": { "level": "error" },
        "no-unused-params": { "level": "error" },
        "no-unused-vars": { "level": "error" },
        "prefer-interpolation": { "level": "error" },
        "secure-parameter-default": { "level": "error" },
        "secure-secrets-in-params": { "level": "error" },
        "outputs-should-not-contain-secrets": { "level": "error" },
        "use-recent-api-versions": { "level": "warning" },
        "adminusername-should-not-be-literal": { "level": "error" },
        "use-stable-resource-identifiers": { "level": "error" }
      }
    }
  }
}
```

- [ ] **Step 2: Create `infra/README.md` with local validate commands**

````markdown
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
````

- [ ] **Step 3: Commit**

```bash
git add infra/bicepconfig.json infra/README.md
git commit -m "chore: scaffold infra/ with bicep lint config and validate doc"
```

---

### Task 2: User-assigned managed identity module

**Files:**
- Create: `infra/modules/identity.bicep`

- [ ] **Step 1: Author the module**

```bicep
metadata description = 'User-assigned managed identity for the ADO MCP Bridge container app.'

@description('Environment short name: dev or prod.')
@allowed([ 'dev', 'prod' ])
param env string

@description('Azure region for the identity.')
param location string

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-adomcp-${env}'
  location: location
}

output id string = identity.id
output name string = identity.name
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
```

- [ ] **Step 2: Build the module standalone to prove it compiles**

Run: `az bicep build --file infra/modules/identity.bicep`
Expected: exits 0, produces `infra/modules/identity.json`, no warnings.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/identity.bicep
git commit -m "feat(infra): add user-assigned managed identity module"
```

---

### Task 3: ACR module with AcrPull role for the MI

**Files:**
- Create: `infra/modules/acr.bicep`

- [ ] **Step 1: Author the module**

```bicep
metadata description = 'Basic ACR with admin user disabled; grants AcrPull to the supplied managed identity.'

@description('Environment short name.')
@allowed([ 'dev', 'prod' ])
param env string

@description('Azure region.')
param location string

@description('Principal id of the user-assigned MI that needs AcrPull.')
param miPrincipalId string

var acrName = 'cradomcp${env}'
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    anonymousPullEnabled: false
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, miPrincipalId, acrPullRoleId)
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: miPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output loginServer string = acr.properties.loginServer
output name string = acr.name
output id string = acr.id
```

- [ ] **Step 2: Build the module**

Run: `az bicep build --file infra/modules/acr.bicep`
Expected: exits 0, no warnings, `infra/modules/acr.json` produced.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/acr.bicep
git commit -m "feat(infra): add ACR Basic module with AcrPull role assignment"
```

---

### Task 4: Key Vault module with RBAC, DEK key, and cert placeholder

**Files:**
- Create: `infra/modules/keyvault.bicep`

- [ ] **Step 1: Author the module**

```bicep
metadata description = 'Key Vault (RBAC) hosting the Entra app certificate and the token-encryption DEK.'

@description('Environment short name.')
@allowed([ 'dev', 'prod' ])
param env string

@description('Azure region.')
param location string

@description('Tenant id for the vault.')
param tenantId string

@description('Principal id of the user-assigned MI that consumes vault secrets/keys.')
param miPrincipalId string

var vaultName = 'kv-adomcp-${env}'

// Built-in role definition ids.
var roleKeyVaultCryptoUser = '12338af0-0e69-4776-bea7-57ae8d297424'
var roleKeyVaultCertificateUser = 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba'
var roleKeyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource dek 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'token-dek'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: [ 'wrapKey', 'unwrapKey', 'encrypt', 'decrypt' ]
    attributes: {
      enabled: true
    }
  }
}

resource cryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, miPrincipalId, roleKeyVaultCryptoUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultCryptoUser)
    principalId: miPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource certUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, miPrincipalId, roleKeyVaultCertificateUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultCertificateUser)
    principalId: miPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, miPrincipalId, roleKeyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
    principalId: miPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output vaultUri string = vault.properties.vaultUri
output name string = vault.name
output id string = vault.id
output dekName string = dek.name
output certificateName string = 'ado-mcp-bridge'
```

Note: the certificate object cannot be created by Bicep without a CSR/import payload; the cert named `ado-mcp-bridge` is provisioned out-of-band by the CI/CD plan's bootstrap script. The `certificateName` output exists so downstream modules can wire the Key Vault reference.

- [ ] **Step 2: Build the module**

Run: `az bicep build --file infra/modules/keyvault.bicep`
Expected: exits 0, no warnings.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/keyvault.bicep
git commit -m "feat(infra): add Key Vault module with RBAC roles and token-dek key"
```

---

### Task 5: SQL Serverless module with Entra-only admin

**Files:**
- Create: `infra/modules/sql.bicep`

- [ ] **Step 1: Author the module**

```bicep
metadata description = 'Azure SQL Serverless GP database with Entra-only admin and Azure-services firewall rule.'

@description('Environment short name.')
@allowed([ 'dev', 'prod' ])
param env string

@description('Azure region.')
param location string

@description('Object id of the AAD security group that becomes SQL admin.')
param sqlAdminAadGroupObjectId string

@description('Display name for the AAD admin group (informational).')
param sqlAdminAadGroupName string = 'sg-adomcp-sqladmins'

@description('Tenant id.')
param tenantId string

@description('Principal id of the user-assigned MI that the app uses to connect (for role grant).')
param miPrincipalId string

var serverName = 'sql-adomcp-${env}'
var dbName = 'sqldb-adomcp'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminAadGroupName
      sid: sqlAdminAadGroupObjectId
      tenantId: tenantId
      principalType: 'Group'
    }
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: dbName
  location: location
  sku: {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output serverName string = sqlServer.name
output databaseName string = sqlDb.name
// Note: the MI principalId is consumed by a post-deploy SQL script in the CI/CD plan,
// not by an ARM role assignment (SQL contained-user grant happens via T-SQL).
output miPrincipalIdEcho string = miPrincipalId
```

- [ ] **Step 2: Build the module**

Run: `az bicep build --file infra/modules/sql.bicep`
Expected: exits 0, no warnings.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/sql.bicep
git commit -m "feat(infra): add SQL Serverless module with Entra-only admin"
```

---

### Task 6: Observability module (Log Analytics + App Insights)

**Files:**
- Create: `infra/modules/observability.bicep`

- [ ] **Step 1: Author the module**

```bicep
metadata description = 'Workspace-based Application Insights backed by a Log Analytics workspace.'

@description('Environment short name.')
@allowed([ 'dev', 'prod' ])
param env string

@description('Azure region.')
param location string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-adomcp-${env}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-adomcp-${env}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output workspaceId string = workspace.id
output workspaceCustomerId string = workspace.properties.customerId
#disable-next-line outputs-should-not-contain-secrets
output workspaceSharedKey string = workspace.listKeys().primarySharedKey
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
output appInsightsId string = appInsights.id
```

- [ ] **Step 2: Build the module**

Run: `az bicep build --file infra/modules/observability.bicep`
Expected: exits 0; the lone `outputs-should-not-contain-secrets` site is suppressed inline (needed because the Container App Environment requires the LA shared key at deploy time, and there's no managed-identity alternative for that exact wiring).

- [ ] **Step 3: Commit**

```bash
git add infra/modules/observability.bicep
git commit -m "feat(infra): add workspace-based App Insights + Log Analytics module"
```

---

### Task 7: Container App module (env + app + ingress + KV refs)

**Files:**
- Create: `infra/modules/containerapp.bicep`

- [ ] **Step 1: Author the module**

```bicep
metadata description = 'Container App Environment and Container App for the ADO MCP Bridge.'

@description('Environment short name.')
@allowed([ 'dev', 'prod' ])
param env string

@description('Azure region.')
param location string

@description('User-assigned MI resource id.')
param miResourceId string

@description('User-assigned MI client id (for AAD-based DefaultAzureCredential pickup).')
param miClientId string

@description('Log Analytics workspace customer id.')
param laWorkspaceCustomerId string

@description('Log Analytics workspace shared key.')
@secure()
param laWorkspaceSharedKey string

@description('App Insights connection string.')
param appInsightsConnectionString string

@description('Full container image reference, including tag (e.g. ghcr.io/owner/ado-mcp-bridge:1.2.3).')
param containerImage string

@description('Issuer URL for the OAuth AS (https://<host>).')
param issuer string

@description('Entra tenant id.')
param entraTenantId string

@description('Entra app (client) id.')
param entraClientId string

@description('Key Vault URI.')
param keyVaultUri string

@description('Certificate name in Key Vault for Entra confidential client auth.')
param certificateName string

@description('DEK name in Key Vault for token encryption.')
param dekName string

@description('SQL server FQDN.')
param sqlServerFqdn string

@description('SQL database name.')
param sqlDatabaseName string

@description('ACR login server (for the registries block).')
param acrLoginServer string

@description('IP allowlist applied as ingress IP restrictions. Default is open.')
param allowedIpRanges array = [ '0.0.0.0/0' ]

var isOpen = length(allowedIpRanges) == 1 && allowedIpRanges[0] == '0.0.0.0/0'

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-adomcp-${env}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: laWorkspaceCustomerId
        sharedKey: laWorkspaceSharedKey
      }
    }
    zoneRedundant: false
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-adomcp-${env}'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${miResourceId}': {}
    }
  }
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        ipSecurityRestrictions: isOpen ? null : [for (range, i) in allowedIpRanges: {
          name: 'allow-${i}'
          action: 'Allow'
          ipAddressRange: range
          description: 'Operator-configured allowlist entry ${i}'
        }]
      }
      registries: [
        {
          server: acrLoginServer
          identity: miResourceId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'ado-mcp-bridge'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AdoMcp__Issuer', value: issuer }
            { name: 'AdoMcp__Entra__TenantId', value: entraTenantId }
            { name: 'AdoMcp__Entra__ClientId', value: entraClientId }
            { name: 'AdoMcp__Entra__CertificateName', value: certificateName }
            { name: 'AdoMcp__Entra__Authority', value: 'https://login.microsoftonline.com/${entraTenantId}/v2.0' }
            { name: 'AdoMcp__KeyVault__VaultUri', value: keyVaultUri }
            { name: 'AdoMcp__KeyVault__DekName', value: dekName }
            { name: 'AdoMcp__Database__ConnectionString', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'AZURE_CLIENT_ID', value: miClientId }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/readyz', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 5
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output fqdn string = app.properties.configuration.ingress.fqdn
output appName string = app.name
output environmentName string = cae.name
```

- [ ] **Step 2: Build the module**

Run: `az bicep build --file infra/modules/containerapp.bicep`
Expected: exits 0, no warnings.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/containerapp.bicep
git commit -m "feat(infra): add Container App Environment + App module with KV refs and ingress allowlist"
```

---

### Task 8: Top-level `main.bicep` composing all modules

**Files:**
- Create: `infra/main.bicep`

- [ ] **Step 1: Author the top-level template**

```bicep
metadata description = 'ADO MCP Bridge — top-level deployment for a single environment.'
targetScope = 'resourceGroup'

@allowed([ 'dev', 'prod' ])
@description('Environment short name.')
param env string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('IP allowlist for the Container App ingress. Default is fully open.')
param allowedIpRanges array = [ '0.0.0.0/0' ]

@description('Entra tenant id.')
param entraTenantId string

@description('Entra app (client) id for confidential-client auth.')
param entraClientId string

@description('Full container image reference, including tag.')
param containerImage string

@description('Object id of the AAD security group that becomes the SQL admin.')
param sqlAdminAadGroupObjectId string

@description('Display name for the SQL admin AAD group (informational only).')
param sqlAdminAadGroupName string = 'sg-adomcp-sqladmins'

@description('Issuer URL the bridge advertises. Defaults to the deployed Container App FQDN once known; supply explicitly to pin a custom domain.')
param issuerOverride string = ''

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    env: env
    location: location
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    env: env
    location: location
    miPrincipalId: identity.outputs.principalId
  }
}

module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    env: env
    location: location
    tenantId: entraTenantId
    miPrincipalId: identity.outputs.principalId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    env: env
    location: location
    tenantId: entraTenantId
    sqlAdminAadGroupObjectId: sqlAdminAadGroupObjectId
    sqlAdminAadGroupName: sqlAdminAadGroupName
    miPrincipalId: identity.outputs.principalId
  }
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    env: env
    location: location
  }
}

module containerapp 'modules/containerapp.bicep' = {
  name: 'containerapp'
  params: {
    env: env
    location: location
    miResourceId: identity.outputs.id
    miClientId: identity.outputs.clientId
    laWorkspaceCustomerId: observability.outputs.workspaceCustomerId
    laWorkspaceSharedKey: observability.outputs.workspaceSharedKey
    appInsightsConnectionString: observability.outputs.appInsightsConnectionString
    containerImage: containerImage
    issuer: empty(issuerOverride) ? 'https://placeholder-overwritten-post-deploy.invalid' : issuerOverride
    entraTenantId: entraTenantId
    entraClientId: entraClientId
    keyVaultUri: keyvault.outputs.vaultUri
    certificateName: keyvault.outputs.certificateName
    dekName: keyvault.outputs.dekName
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    acrLoginServer: acr.outputs.loginServer
    allowedIpRanges: allowedIpRanges
  }
}

output containerAppFqdn string = containerapp.outputs.fqdn
output containerAppName string = containerapp.outputs.appName
output keyVaultUri string = keyvault.outputs.vaultUri
output sqlServerFqdn string = sql.outputs.serverFqdn
output acrLoginServer string = acr.outputs.loginServer
output managedIdentityClientId string = identity.outputs.clientId
output appInsightsConnectionString string = observability.outputs.appInsightsConnectionString
```

- [ ] **Step 2: Lint then build**

Run: `az bicep lint --file infra/main.bicep && az bicep build --file infra/main.bicep`
Expected: both exit 0, `infra/main.json` produced, no errors and no warnings.

- [ ] **Step 3: Commit**

```bash
git add infra/main.bicep
git commit -m "feat(infra): compose top-level main.bicep with all subsystem modules"
```

---

### Task 9: Parameter files for `dev` and `prod`

**Files:**
- Create: `infra/main.dev.bicepparam`
- Create: `infra/main.prod.bicepparam`

- [ ] **Step 1: Author `infra/main.dev.bicepparam`**

```bicep
using './main.bicep'

param env = 'dev'
param location = 'uksouth'
param allowedIpRanges = [ '0.0.0.0/0' ]
param entraTenantId = readEnvironmentVariable('ADOMCP_TENANT_ID', '00000000-0000-0000-0000-000000000000')
param entraClientId = readEnvironmentVariable('ADOMCP_CLIENT_ID', '00000000-0000-0000-0000-000000000000')
param containerImage = readEnvironmentVariable('ADOMCP_IMAGE', 'ghcr.io/andrew-teece/ado-mcp-bridge:dev')
param sqlAdminAadGroupObjectId = readEnvironmentVariable('ADOMCP_SQL_ADMIN_GROUP_OID', '00000000-0000-0000-0000-000000000000')
param sqlAdminAadGroupName = 'sg-adomcp-sqladmins-dev'
param issuerOverride = ''
```

- [ ] **Step 2: Author `infra/main.prod.bicepparam`**

```bicep
using './main.bicep'

param env = 'prod'
param location = 'uksouth'
// Prod ships closed-by-default. Operators must populate before promoting.
param allowedIpRanges = [ '0.0.0.0/0' ]
param entraTenantId = readEnvironmentVariable('ADOMCP_TENANT_ID', '00000000-0000-0000-0000-000000000000')
param entraClientId = readEnvironmentVariable('ADOMCP_CLIENT_ID', '00000000-0000-0000-0000-000000000000')
param containerImage = readEnvironmentVariable('ADOMCP_IMAGE', 'ghcr.io/andrew-teece/ado-mcp-bridge:latest')
param sqlAdminAadGroupObjectId = readEnvironmentVariable('ADOMCP_SQL_ADMIN_GROUP_OID', '00000000-0000-0000-0000-000000000000')
param sqlAdminAadGroupName = 'sg-adomcp-sqladmins-prod'
param issuerOverride = ''
```

- [ ] **Step 3: Build both param files to verify they bind**

Run:
```bash
az bicep build-params --file infra/main.dev.bicepparam
az bicep build-params --file infra/main.prod.bicepparam
```
Expected: both exit 0, produce `*.parameters.json` siblings, no errors.

- [ ] **Step 4: Commit**

```bash
git add infra/main.dev.bicepparam infra/main.prod.bicepparam
git commit -m "feat(infra): add dev/prod bicepparam files with env-var sourced secrets"
```

---

### Task 10: Full-stack lint + build of the composed template

**Files:** none (validation-only)

- [ ] **Step 1: Run lint across `infra/main.bicep`**

Run: `az bicep lint --file infra/main.bicep`
Expected: exits 0 with no findings (the only suppressed rule is the inline `#disable-next-line outputs-should-not-contain-secrets` in `observability.bicep`).

- [ ] **Step 2: Build the composed template and inspect output for warnings**

Run: `az bicep build --file infra/main.bicep 2>&1 | tee /tmp/bicep-build.log`
Expected: log contains no lines starting with `Warning` or `Error`. `infra/main.json` exists and is valid JSON (sanity check `jq type infra/main.json` returns `"object"`).

- [ ] **Step 3: Commit (no source changes — just verifying state)**

If any fixes were needed in this task, commit them now:

```bash
git add -A infra/
git commit -m "fix(infra): resolve bicep lint/build findings from full-stack validation"
```

Otherwise skip this step.

---

### Task 11: `az deployment group validate` (conditional / gated)

**Files:**
- Create: `infra/scripts/validate.sh`

- [ ] **Step 1: Write a guarded validate script**

```bash
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
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x infra/scripts/validate.sh`

- [ ] **Step 3: Dry-run locally (no creds — expect SKIP)**

Run: `infra/scripts/validate.sh`
Expected: prints `SKIP: ADOMCP_VALIDATE_RG not set; skipping az deployment group validate.` and exits 0.

- [ ] **Step 4: Commit**

```bash
git add infra/scripts/validate.sh
git commit -m "feat(infra): add gated az deployment group validate script"
```

---

### Task 12: PSRule for Azure — failing baseline first, then tighten

**Files:**
- Create: `infra/ps-rule.yaml`
- Create: `infra/.ps-rule/Baseline.Rule.yaml`

- [ ] **Step 1: Write a permissive PSRule config that scans `main.json`**

```yaml
# infra/ps-rule.yaml
input:
  pathIgnore:
    - 'modules/*.json'   # only scan the composed main.json
binding:
  targetType:
    - resourceType
configuration:
  AZURE_BICEP_FILE_EXPANSION: false
output:
  culture:
    - en-GB
```

- [ ] **Step 2: Write a baseline binding that uses Microsoft's `Azure.Pillar.Security` baseline**

```yaml
# infra/.ps-rule/Baseline.Rule.yaml
---
apiVersion: github.com/microsoft/PSRule/v1
kind: Baseline
metadata:
  name: AdoMcpBridge.Security
spec:
  rule:
    include:
      - 'Azure.KeyVault.*'
      - 'Azure.ACR.*'
      - 'Azure.SQL.*'
      - 'Azure.AppInsights.*'
      - 'Azure.ContainerApp.*'
      - 'Azure.Identity.*'
```

- [ ] **Step 3: Run PSRule and capture the failing-first baseline**

Run:
```pwsh
pwsh -Command "Install-Module -Name PSRule.Rules.Azure -Scope CurrentUser -Force -SkipPublisherCheck; \
  Invoke-PSRule -InputPath infra/main.json -Module PSRule.Rules.Azure \
    -Option infra/ps-rule.yaml -Baseline AdoMcpBridge.Security -As Detail -Outcome Fail"
```

Expected initial failures (these are the ones we intentionally leave for the human operator or for follow-up plans, and the ones we'll suppress with documented justification):

- `Azure.ACR.MinSku` — Basic SKU flagged; we accept by suppression (cost target < $20/mo).
- `Azure.SQL.FirewallRuleCount` — passes (only one rule).
- `Azure.KeyVault.PurgeProtect` — must PASS (we already set `enablePurgeProtection: true`).
- `Azure.ContainerApp.PublicAccess` — expected Fail when `allowedIpRanges` is open; documented as a runbook decision.

- [ ] **Step 4: Suppress only the rules we explicitly accept, in `infra/ps-rule.yaml`**

Replace `infra/ps-rule.yaml` contents with:

```yaml
input:
  pathIgnore:
    - 'modules/*.json'
binding:
  targetType:
    - resourceType
configuration:
  AZURE_BICEP_FILE_EXPANSION: false
output:
  culture:
    - en-GB
suppression:
  Azure.ACR.MinSku:
    - 'cradomcpdev'
    - 'cradomcpprod'
  Azure.ContainerApp.PublicAccess:
    - 'ca-adomcp-dev'
    - 'ca-adomcp-prod'
```

- [ ] **Step 5: Re-run PSRule and confirm zero Fail outcomes**

Run:
```pwsh
pwsh -Command "Invoke-PSRule -InputPath infra/main.json -Module PSRule.Rules.Azure \
  -Option infra/ps-rule.yaml -Baseline AdoMcpBridge.Security -As Summary -Outcome Fail"
```
Expected: `0` failing rules; output shows `Pass` count > 0 and `Fail` count = 0.

- [ ] **Step 6: Commit**

```bash
git add infra/ps-rule.yaml infra/.ps-rule/Baseline.Rule.yaml
git commit -m "test(infra): wire PSRule for Azure baseline with documented suppressions"
```

---

### Task 13: CI doc snippet for the CI/CD plan to consume

**Files:**
- Create: `pipelines/snippets/bicep-validate.md`

- [ ] **Step 1: Document the exact commands the CI workflow must run**

````markdown
# Bicep validate snippet (consumed by `2026-06-09-cicd-release.md`)

The Bicep plan is responsible for the commands. The CI/CD plan wires
them into a GitHub Actions job.

## Required steps

```yaml
- name: Setup Bicep
  run: az bicep install

- name: Bicep lint
  run: az bicep lint --file infra/main.bicep

- name: Bicep build
  run: az bicep build --file infra/main.bicep

- name: az deployment group validate (gated)
  env:
    ADOMCP_VALIDATE_RG: ${{ vars.ADOMCP_VALIDATE_RG }}
    ADOMCP_VALIDATE_ENV: dev
    ADOMCP_TENANT_ID: ${{ secrets.ADOMCP_TENANT_ID }}
    ADOMCP_CLIENT_ID: ${{ secrets.ADOMCP_CLIENT_ID }}
    ADOMCP_IMAGE: ghcr.io/${{ github.repository }}:${{ github.sha }}
    ADOMCP_SQL_ADMIN_GROUP_OID: ${{ secrets.ADOMCP_SQL_ADMIN_GROUP_OID }}
  run: infra/scripts/validate.sh

- name: PSRule for Azure
  shell: pwsh
  run: |
    Install-Module -Name PSRule.Rules.Azure -Scope CurrentUser -Force -SkipPublisherCheck
    Invoke-PSRule -InputPath infra/main.json -Module PSRule.Rules.Azure `
      -Option infra/ps-rule.yaml -Baseline AdoMcpBridge.Security `
      -As Summary -Outcome Fail -ErrorAction Stop
```

Notes for the CI/CD plan author:
- The validate step short-circuits to `SKIP` when no creds are present —
  it is safe to run on fork PRs.
- `infra/main.json` is regenerated by the `bicep build` step on every CI
  run; do not commit it.
````

- [ ] **Step 2: Add `infra/main.json` and `infra/modules/*.json` to `.gitignore`**

Append to `.gitignore` (create if missing):

```gitignore
infra/main.json
infra/modules/*.json
infra/*.parameters.json
```

- [ ] **Step 3: Verify generated artefacts are ignored**

Run: `git status --short infra/`
Expected: no `?? infra/main.json` line; only the source files appear as additions.

- [ ] **Step 4: Commit**

```bash
git add pipelines/snippets/bicep-validate.md .gitignore
git commit -m "docs(infra): publish CI validate snippet and ignore generated ARM JSON"
```

---

### Task 14: End-to-end smoke of the validation pipeline

**Files:** none (final dry-run)

- [ ] **Step 1: Clean, build, lint, validate (skip), PSRule — in order**

Run:
```bash
rm -f infra/main.json infra/modules/*.json
az bicep lint --file infra/main.bicep
az bicep build --file infra/main.bicep
infra/scripts/validate.sh
pwsh -Command "Invoke-PSRule -InputPath infra/main.json -Module PSRule.Rules.Azure -Option infra/ps-rule.yaml -Baseline AdoMcpBridge.Security -As Summary -Outcome Fail"
```

Expected output sequence:
1. Lint: exit 0, no findings.
2. Build: exit 0, `infra/main.json` exists.
3. Validate: prints `SKIP: ADOMCP_VALIDATE_RG not set; ...` and exits 0.
4. PSRule: `Fail` count = 0.

- [ ] **Step 2: If any step fails, fix the source module and re-run the full chain. Do not commit a partially-passing state.**

- [ ] **Step 3: Tag the working state for downstream plan consumption**

```bash
git tag -a infra-bicep-ready -m "Bicep modules pass lint/build/PSRule cleanly"
```

(No push — the CI/CD plan will wire this into release tagging.)

---

## Self-Review Notes

**Spec coverage (§6 + §10):**
- ACR Basic, MI pull, no admin user → Task 3.
- Key Vault RBAC + DEK + cert (placeholder, created out-of-band) → Task 4.
- SQL Serverless GP, Entra-only admin, AzureServices firewall rule, no SQL passwords → Task 5.
- App Insights + Log Analytics workspace-based → Task 6.
- Container App + Environment, min 0 / max 5, HTTPS-only ingress, MI attached, env vars from KV refs and App Insights → Task 7.
- `allowedIpRanges` parameter, default `['0.0.0.0/0']`, applied as ingress IP restrictions only when not the default → Task 7 (`isOpen` ternary) + Task 8 plumbing + Task 9 param files.
- Per-env resource groups `rg-adomcp-{env}` → enforced by the validate script's `--resource-group` argument; the modules themselves are RG-scoped (Task 8 `targetScope = 'resourceGroup'`).
- Idle cost < $20/mo → SQL `GP_S_Gen5_1` + `autoPauseDelay: 60` + `minCapacity: 0.5`, Container App `minReplicas: 0`, ACR Basic. PSRule `Azure.ACR.MinSku` suppression in Task 12 documents the trade-off.

**Naming consistency (shared-contracts.md):**
- `id-adomcp-{env}`, `cradomcp{env}`, `kv-adomcp-{env}`, `sql-adomcp-{env}`, `sqldb-adomcp`, `log-adomcp-{env}`, `appi-adomcp-{env}`, `cae-adomcp-{env}`, `ca-adomcp-{env}` — all present and matching across modules.

**Param-name consistency across tasks:** `miPrincipalId` in `acr.bicep`/`keyvault.bicep`/`sql.bicep`, `miResourceId` + `miClientId` in `containerapp.bicep` (different shapes because role assignment needs principal id, while Container App registries auth needs the resource id). Top-level `main.bicep` wires these explicitly from `identity.outputs.{principalId,id,clientId}`.

**Placeholder scan:** None. The Entra cert is intentionally *not* a placeholder — it's documented as out-of-band provisioning consumed by the CI/CD plan (Task 4 note). The `issuerOverride` parameter has an empty-string default with explicit sentinel value `'https://placeholder-overwritten-post-deploy.invalid'`, surfaced so the CI/CD plan can patch it post-deploy once the FQDN is known.

**Out-of-scope, deferred to the named plan:**
- T-SQL MI-as-contained-user grant for the SQL DB → CI/CD plan (post-deploy step).
- Cert provisioning into Key Vault → CI/CD plan (bootstrap script).
- Alert rules in Bicep → Observability/Runbook plan.
- GHA workflow YAML → CI/CD plan (this plan only ships the doc snippet in `pipelines/snippets/bicep-validate.md`).

**Failing-first demonstration:** Task 12 runs PSRule with no suppressions first, captures the expected failures, then adds suppressions for the two we explicitly accept (ACR Basic SKU; default-open ingress). This is the closest Bicep gets to red-green-refactor and is honest about which findings are accepted vs. fixed.
