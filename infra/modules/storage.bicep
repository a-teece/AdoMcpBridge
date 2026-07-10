metadata description = 'Storage account hosting upload slots for large ADO field writes.'

@allowed([ 'dev', 'prod' ])
@description('Environment short name.')
param env string

@description('Azure region.')
param location string

@description('Principal id of the user-assigned MI that reads/writes/deletes blobs and issues user-delegation SAS URLs.')
param miPrincipalId string

var storageName = 'stadomcp${env}'

// Built-in role definition ids.
var roleStorageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var roleStorageBlobDelegator      = 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Disabling shared-key access forces all callers to authenticate via
    // Azure AD (MI or SAS signed by a user-delegation key — no account key
    // material ever leaves the control plane).
    allowSharedKeyAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    // Slots are ephemeral scratch; soft delete would accumulate stale blobs.
    deleteRetentionPolicy: { enabled: false }
  }
}

resource slotsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'upload-slots'
  properties: { publicAccess: 'None' }
}

// MI reads, writes, and deletes blobs during slot lifecycle.
resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, miPrincipalId, roleStorageBlobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDataContributor)
    principalId: miPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// MI issues user-delegation keys so SAS URLs are signed by the MI identity
// (no account key ever materialises in the application).
resource blobDelegator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, miPrincipalId, roleStorageBlobDelegator)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDelegator)
    principalId: miPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// A lifecycle policy deletes unclaimed slots after 1 day (safety net for
// clients that call create_upload_slot but never follow up with
// write_field_from_slot — the bridge deletes on success, so this handles
// only abandoned slots).
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'expire-abandoned-slots'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: [ 'blockBlob' ], prefixMatch: [ 'upload-slots/' ] }
            actions: { baseBlob: { delete: { daysAfterCreationGreaterThan: 1 } } }
          }
        }
      ]
    }
  }
}

output blobEndpointUri string = storage.properties.primaryEndpoints.blob
output containerName string = slotsContainer.name
