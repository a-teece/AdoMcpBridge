#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploys the ADO MCP Bridge to an Azure subscription.

.DESCRIPTION
    Wraps `az deployment group create` against infra/main.bicep with the
    per-environment .bicepparam file. Before deploying, verifies that
    the requested GHCR image+tag exists and carries a valid cosign
    signature.

.PARAMETER Env
    Target environment: dev or prod.

.PARAMETER Tag
    Bridge version tag to deploy (e.g. v0.1.0). Must match GHCR image tag.

.PARAMETER SubscriptionId
    Azure subscription GUID.

.PARAMETER ResourceGroup
    Target resource group (e.g. rg-adomcp-prod).

.PARAMETER ImageOwner
    GHCR namespace; defaults to the upstream "AdoMcpBridge" owner.

.EXAMPLE
    ./deploy.ps1 -Env prod -Tag v0.1.0 `
        -SubscriptionId 11111111-1111-1111-1111-111111111111 `
        -ResourceGroup rg-adomcp-prod
#>
[CmdletBinding()]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSReviewUnusedParameter', '',
    Justification = 'Script-level params are consumed by Invoke-Deploy via script scope; PSScriptAnalyzer cannot follow that flow.')]
param(
    [Parameter(Mandatory)] [ValidateSet('dev', 'prod')] [string] $Env,
    [Parameter(Mandatory)] [ValidatePattern('^v\d+\.\d+\.\d+(-[A-Za-z0-9\.\-]+)?$')] [string] $Tag,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9a-fA-F-]{36}$')] [string] $SubscriptionId,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ResourceGroup,
    [Parameter()] [string] $ImageOwner = 'a-teece'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Command {
    param([Parameter(Mandatory)][string] $Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' not found on PATH."
    }
}

function Invoke-PreflightVerify {
    param(
        [Parameter(Mandatory)][string] $ImageRef,
        [Parameter(Mandatory)][string] $RepoUrl
    )

    Write-Information "Verifying cosign signature on $ImageRef ..." -InformationAction Continue
    & cosign verify `
        --certificate-identity-regexp "^https://github\.com/$RepoUrl/\.github/workflows/release\.yml@refs/tags/" `
        --certificate-oidc-issuer 'https://token.actions.githubusercontent.com' `
        $ImageRef | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "cosign verify failed for $ImageRef."
    }
}

function Invoke-Deploy {
    Assert-Command 'az'
    Assert-Command 'cosign'

    $imageRef = "ghcr.io/$ImageOwner/adomcpbridge:$Tag"
    $repoUrl = "$ImageOwner/AdoMcpBridge"

    Invoke-PreflightVerify -ImageRef $imageRef -RepoUrl $repoUrl

    Write-Information "Setting subscription $SubscriptionId ..." -InformationAction Continue
    & az account set --subscription $SubscriptionId | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "az account set failed." }

    $paramFile = Join-Path $PSScriptRoot "infra/main.$Env.bicepparam"
    if (-not (Test-Path $paramFile)) {
        throw "Parameter file not found: $paramFile"
    }

    Write-Information "Deploying $imageRef to $ResourceGroup ..." -InformationAction Continue
    & az deployment group create `
        --resource-group $ResourceGroup `
        --template-file (Join-Path $PSScriptRoot 'infra/main.bicep') `
        --parameters $paramFile `
        --parameters "containerImage=$imageRef" `
        --name "adomcp-$Tag-$(Get-Date -Format yyyyMMddHHmmss)"

    if ($LASTEXITCODE -ne 0) { throw "az deployment group create failed." }

    Write-Information "Deployment complete." -InformationAction Continue
}

# Skip execution when dot-sourced (so Pester can test the functions).
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-Deploy
}
