metadata description = 'User-assigned managed identity for the ADO MCP Bridge container app.'

@description('Environment short name: dev or prod.')
@allowed([ 'dev', 'prod' ])
param env string

@description('Azure region for the identity.')
param location string

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-adomcp-${env}'
  location: location
}

output id string = identity.id
output name string = identity.name
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
