#Requires -Version 7.0
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    $script:DeployScript = Join-Path $PSScriptRoot '../../deploy.ps1'
    if (-not (Test-Path $script:DeployScript)) {
        throw "deploy.ps1 not found at $script:DeployScript"
    }
    . $script:DeployScript
}

Describe 'deploy.ps1 parameter validation' {
    It 'rejects an invalid environment' {
        { & $script:DeployScript -Env 'staging' -Tag 'v0.1.0' `
            -SubscriptionId '11111111-1111-1111-1111-111111111111' `
            -ResourceGroup 'rg-adomcp-staging' } |
            Should -Throw -ErrorId 'ParameterArgumentValidationError*'
    }

    It 'rejects a non-SemVer tag' {
        { & $script:DeployScript -Env 'dev' -Tag '0.1' `
            -SubscriptionId '11111111-1111-1111-1111-111111111111' `
            -ResourceGroup 'rg-adomcp-dev' } |
            Should -Throw -ErrorId 'ParameterArgumentValidationError*'
    }

    It 'rejects a non-GUID subscription' {
        { & $script:DeployScript -Env 'dev' -Tag 'v0.1.0' `
            -SubscriptionId 'not-a-guid' `
            -ResourceGroup 'rg-adomcp-dev' } |
            Should -Throw -ErrorId 'ParameterArgumentValidationError*'
    }
}

Describe 'Assert-Command' {
    It 'throws when the named command is missing' {
        { Assert-Command -Name 'definitely-not-a-real-command-xyz' } |
            Should -Throw "*not found on PATH*"
    }

    It 'returns silently when the named command exists' {
        { Assert-Command -Name 'pwsh' } | Should -Not -Throw
    }
}

Describe 'Invoke-PreflightVerify' {
    BeforeAll {
        function global:cosign { $global:LASTEXITCODE = 0 }
    }

    AfterAll {
        Remove-Item Function:\cosign -ErrorAction SilentlyContinue
    }

    It 'throws when cosign exits non-zero' {
        function global:cosign { $global:LASTEXITCODE = 7 }
        { Invoke-PreflightVerify -ImageRef 'ghcr.io/x/y:v0.0.1' -RepoUrl 'x/y' } |
            Should -Throw "*cosign verify failed*"
    }

    It 'returns silently when cosign exits zero' {
        function global:cosign { $global:LASTEXITCODE = 0 }
        { Invoke-PreflightVerify -ImageRef 'ghcr.io/x/y:v0.0.1' -RepoUrl 'x/y' } |
            Should -Not -Throw
    }
}
