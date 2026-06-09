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

resource sqlServer 'Microsoft.Sql/servers@2025-01-01' = {
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

resource sqlDb 'Microsoft.Sql/servers/databases@2025-01-01' = {
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

resource allowAzure 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
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
