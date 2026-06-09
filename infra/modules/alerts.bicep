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
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

resource certExpiryAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-cert-expiry'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ keyVaultId ]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT1H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'CertNearExpiry'
          metricNamespace: 'Microsoft.KeyVault/vaults'
          metricName: 'CertificateNearExpiry'
          operator: 'LessThan'
          threshold: 14
          timeAggregation: 'Minimum'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}
