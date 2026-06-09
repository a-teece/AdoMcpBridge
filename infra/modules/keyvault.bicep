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

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
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

resource dek 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
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
