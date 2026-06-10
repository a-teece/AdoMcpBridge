# Changelog

All notable changes to **ADO MCP Bridge** are documented here.
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
and the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
`release-please` maintains entries below this line from Conventional
Commit messages — do not hand-edit released sections.

## [0.1.2](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.1...v0.1.2) (2026-06-10)


### Bug Fixes

* accept http loopback redirect URIs in DCR per RFC 8252 ([#30](https://github.com/a-teece/AdoMcpBridge/issues/30)) ([22a9e9e](https://github.com/a-teece/AdoMcpBridge/commit/22a9e9e975e9614cf2a52bdd2b2ff39410843924))

## [0.1.1](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.0...v0.1.1) (2026-06-10)


### Bug Fixes

* alerts module fails on fresh deployments ([#26](https://github.com/a-teece/AdoMcpBridge/issues/26)) ([e8d10d0](https://github.com/a-teece/AdoMcpBridge/commit/e8d10d0835a13bee311bf747de15ab5356743255))
* register data and Entra services so the container can start ([#27](https://github.com/a-teece/AdoMcpBridge/issues/27)) ([cf81e6b](https://github.com/a-teece/AdoMcpBridge/commit/cf81e6b0d3782e64cce2923748163edb5f3123db))

## 0.1.0 (2026-06-09)


### Features

* CI/CD pipelines, release automation, and deploy.ps1 ([#10](https://github.com/a-teece/AdoMcpBridge/issues/10)) ([2ca06a1](https://github.com/a-teece/AdoMcpBridge/commit/2ca06a1ba5aeb96655681f4e2a59b11417131ba5))
* Entra token client + Key Vault cert provider ([#7](https://github.com/a-teece/AdoMcpBridge/issues/7)) ([e5e014a](https://github.com/a-teece/AdoMcpBridge/commit/e5e014a613bfa73e0046d9f118ce6bade7889d50))
* foundation skeleton, analyzers, shared abstractions ([#5](https://github.com/a-teece/AdoMcpBridge/issues/5)) ([c6440f2](https://github.com/a-teece/AdoMcpBridge/commit/c6440f28a6ce9faf113e484e40278add0cbcd663))
* **infra:** add Bicep module set for all Azure resources ([#11](https://github.com/a-teece/AdoMcpBridge/issues/11)) ([b3c3acd](https://github.com/a-teece/AdoMcpBridge/commit/b3c3acddd7fca8686932f4507266d0f89db33800))
* OAuth Authorization Server subsystem ([#8](https://github.com/a-teece/AdoMcpBridge/issues/8)) ([390a7f7](https://github.com/a-teece/AdoMcpBridge/commit/390a7f7bef3993a0abafcd397d3409bdd6474d09))
* observability (OpenTelemetry, error handling, alerts, runbook) ([#12](https://github.com/a-teece/AdoMcpBridge/issues/12)) ([8ed5830](https://github.com/a-teece/AdoMcpBridge/commit/8ed58305cd6366db06f1afe43eeb1d25db64623e))
* smoke tests + connector card coverage ([#13](https://github.com/a-teece/AdoMcpBridge/issues/13)) ([e6f2586](https://github.com/a-teece/AdoMcpBridge/commit/e6f2586d8399685a0d3bcdb0a7faf0a7329b70b7))
* token store (EF Core + Key Vault DEK encryptor) ([#6](https://github.com/a-teece/AdoMcpBridge/issues/6)) ([4ea4050](https://github.com/a-teece/AdoMcpBridge/commit/4ea4050ce2774403a1a4fe13b8fe4e821a1a0232))
* YARP MCP reverse-proxy subsystem ([#9](https://github.com/a-teece/AdoMcpBridge/issues/9)) ([5d52ca2](https://github.com/a-teece/AdoMcpBridge/commit/5d52ca2cf21e477625678f6e07758d3bdb6c90ff))


### Documentation

* update README for the implemented bridge ([#19](https://github.com/a-teece/AdoMcpBridge/issues/19)) ([50c73c9](https://github.com/a-teece/AdoMcpBridge/commit/50c73c9af30e2ed588ecff50b3076492d4bdc4e4))

## [Unreleased]

### Added
- Initial scaffolding.
