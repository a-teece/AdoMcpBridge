using './main.bicep'

param env = 'dev'
param location = 'uksouth'
param allowedIpRanges = [ '0.0.0.0/0' ]
param entraTenantId = readEnvironmentVariable('ADOMCP_TENANT_ID', '00000000-0000-0000-0000-000000000000')
param entraClientId = readEnvironmentVariable('ADOMCP_CLIENT_ID', '00000000-0000-0000-0000-000000000000')
param containerImage = readEnvironmentVariable('ADOMCP_IMAGE', 'ghcr.io/a-teece/adomcpbridge:dev')
param sqlAdminAadGroupObjectId = readEnvironmentVariable('ADOMCP_SQL_ADMIN_GROUP_OID', '00000000-0000-0000-0000-000000000000')
param sqlAdminAadGroupName = 'sg-adomcp-sqladmins-dev'
param issuerOverride = ''
