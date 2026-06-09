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

resource acr 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
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
