@description('Deploys the five Observability alert rules paired with runbook scenarios.')
param location string
param actionGroupId string
param appInsightsId string
param logAnalyticsWorkspaceId string
param keyVaultId string
param environmentName string

var prefix = 'adomcp-${environmentName}'

resource internalErrorAlert 'Microsoft.Insights/scheduledQueryRules@2026-03-01' = {
  name: '${prefix}-internal-error'
  location: location
  properties: {
    displayName: 'Any internal_error in 5 min'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [ logAnalyticsWorkspaceId ]
    criteria: {
      allOf: [
        {
          query: 'AppTraces | where SeverityLevel == 3 | where Message has "internal_error"'
          operator: 'GreaterThanOrEqual'
          threshold: 1
          timeAggregation: 'Count'
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

resource tokenRejectionAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-token-rejection-rate'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'TokenRejectRatio'
          metricNamespace: 'azure.applicationinsights'
          metricName: 'oauth.token.rejected'
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
          // Custom metric does not exist until the app first emits it;
          // without this, alert creation fails on a fresh environment.
          skipMetricValidation: true
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

resource upstreamErrorAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-upstream-error-rate'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'UpstreamErrorPct'
          metricNamespace: 'azure.applicationinsights'
          metricName: 'proxy.upstream.errors'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
          skipMetricValidation: true
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

resource entraRefreshLatencyAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-entra-refresh-p95'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'EntraRefreshP95'
          metricNamespace: 'azure.applicationinsights'
          metricName: 'entra.refresh.duration_ms'
          operator: 'GreaterThan'
          threshold: 2000
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
          skipMetricValidation: true
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

// CertificateNearExpiry is an Event Grid event type, not a Key Vault
// platform metric, so it cannot be a metric alert. Key Vault emits the
// event ~30 days before expiry; the MonitorAlert destination (supported
// for Key Vault system events) turns it into an Azure Monitor alert
// wired to the action group.
resource keyVaultEventsTopic 'Microsoft.EventGrid/systemTopics@2025-02-15' = {
  name: '${prefix}-kv-events'
  location: location
  properties: {
    source: keyVaultId
    topicType: 'Microsoft.KeyVault.vaults'
  }
}

resource certExpiryAlert 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2025-02-15' = {
  parent: keyVaultEventsTopic
  name: '${prefix}-cert-expiry'
  properties: {
    // MonitorAlert only supports the CloudEvents v1.0 schema.
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    filter: {
      includedEventTypes: [
        'Microsoft.KeyVault.CertificateNearExpiry'
      ]
    }
    destination: {
      endpointType: 'MonitorAlert'
      properties: {
        severity: 'Sev2'
        actionGroups: [ actionGroupId ]
        description: 'Key Vault certificate near expiry — see runbook scenario 5.'
      }
    }
  }
}
