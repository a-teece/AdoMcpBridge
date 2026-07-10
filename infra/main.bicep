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
    blobStorageAccountUri: storage.outputs.blobEndpointUri
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    env: env
    location: location
    miPrincipalId: identity.outputs.principalId
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    location: location
    actionGroupId: observability.outputs.actionGroupId
    appInsightsId: observability.outputs.appInsightsId
    logAnalyticsWorkspaceId: observability.outputs.workspaceId
    keyVaultId: keyvault.outputs.id
    environmentName: env
  }
}

output containerAppFqdn string = containerapp.outputs.fqdn
output containerAppName string = containerapp.outputs.appName
output keyVaultUri string = keyvault.outputs.vaultUri
output sqlServerFqdn string = sql.outputs.serverFqdn
output acrLoginServer string = acr.outputs.loginServer
output managedIdentityClientId string = identity.outputs.clientId
output appInsightsConnectionString string = observability.outputs.appInsightsConnectionString
output storageAccountUri string = storage.outputs.blobEndpointUri
