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

@description('Full container image reference, including tag (e.g. ghcr.io/owner/adomcpbridge:1.2.3).')
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
var ipRestrictions = [for (range, i) in allowedIpRanges: {
  name: 'allow-${i}'
  action: 'Allow'
  ipAddressRange: range
  description: 'Operator-configured allowlist entry ${i}'
}]

resource cae 'Microsoft.App/managedEnvironments@2025-01-01' = {
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

resource app 'Microsoft.App/containerApps@2025-01-01' = {
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
        ipSecurityRestrictions: isOpen ? null : ipRestrictions
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
            { name: 'AdoMcp__Entra__Authority', value: '${environment().authentication.loginEndpoint}${entraTenantId}/v2.0' }
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
              // App currently exposes only /healthz; readiness shares it
              // until a dedicated /readyz endpoint ships.
              type: 'Readiness'
              httpGet: { path: '/healthz', port: 8080 }
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
