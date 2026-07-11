# Changelog

All notable changes to **ADO MCP Bridge** are documented here.
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
and the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
`release-please` maintains entries below this line from Conventional
Commit messages — do not hand-edit released sections.

## [0.1.12](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.11...v0.1.12) (2026-07-11)


### Bug Fixes

* reintroduce entity-escaping for format=markdown ([#57](https://github.com/a-teece/AdoMcpBridge/issues/57)) ([10f79eb](https://github.com/a-teece/AdoMcpBridge/commit/10f79eb071db2cf018118caee9fc4ea1cb4ac75f))

## [0.1.11](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.10...v0.1.11) (2026-07-11)


### Features

* add format parameter to ado_bridge_write_field_from_slot ([427eb02](https://github.com/a-teece/AdoMcpBridge/commit/427eb02b188950e918933ce3b3dcbd7f00438554))

## [0.1.10](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.9...v0.1.10) (2026-07-11)


### Features

* add MCP readOnlyHint annotations to all bridge custom tools ([#53](https://github.com/a-teece/AdoMcpBridge/issues/53)) ([b673bb4](https://github.com/a-teece/AdoMcpBridge/commit/b673bb4859954120435ef8a83ef53a6f9d47c034))

## [0.1.9](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.8...v0.1.9) (2026-07-10)


### Features

* add ado_bridge_wit_get and ado_bridge_wit_get_batch tools ([#51](https://github.com/a-teece/AdoMcpBridge/issues/51)) ([427fdeb](https://github.com/a-teece/AdoMcpBridge/commit/427fdeb45202b214501fd9740bd21ebb28239d1e))

## [0.1.8](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.7...v0.1.8) (2026-07-10)


### Features

* add custom MCP tools for large ADO field read/write via blob storage ([#49](https://github.com/a-teece/AdoMcpBridge/issues/49)) ([291f2d9](https://github.com/a-teece/AdoMcpBridge/commit/291f2d94a11e4aed7141d7d35e0ace5baf73bd29))

## [0.1.7](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.6...v0.1.7) (2026-06-12)


### Miscellaneous Chores

* mark v0.1.6 as unpublished and cut v0.1.7 ([#47](https://github.com/a-teece/AdoMcpBridge/issues/47)) ([8196b34](https://github.com/a-teece/AdoMcpBridge/commit/8196b34ee515b6adde99f31558a38c142c991cf8))

## [0.1.6](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.5...v0.1.6) (2026-06-11)

> **Note:** v0.1.6 published no artifacts — its release build failed before checkout (#46). Use v0.1.7, which contains the same changes.


### Features

* persist authorization sessions in SQL ([#45](https://github.com/a-teece/AdoMcpBridge/issues/45)) ([91ffc4d](https://github.com/a-teece/AdoMcpBridge/commit/91ffc4d5ea79f954f4b8c81cfd29d4bfc45e23db))


### Bug Fixes

* request the MCP server scope, not the classic ADO scope ([#39](https://github.com/a-teece/AdoMcpBridge/issues/39)) ([92b1e3a](https://github.com/a-teece/AdoMcpBridge/commit/92b1e3a8697004f5441616f578660e6a125a83f2))
* serve RFC 9728 metadata and own every 401 challenge ([#44](https://github.com/a-teece/AdoMcpBridge/issues/44)) ([5758f8d](https://github.com/a-teece/AdoMcpBridge/commit/5758f8d92e9eb1a759d37ca970eeab1722996392))

## [0.1.5](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.4...v0.1.5) (2026-06-10)


### Bug Fixes

* envelope-encrypt refresh tokens; RSA cannot hold a 2 KB payload ([#37](https://github.com/a-teece/AdoMcpBridge/issues/37)) ([cddfadc](https://github.com/a-teece/AdoMcpBridge/commit/cddfadccad38c19836eb3b7ce635ebd5d760b173))

## [0.1.4](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.3...v0.1.4) (2026-06-10)


### Bug Fixes

* supply Entra KeyVaultUri and OAuth scopes to the container ([#35](https://github.com/a-teece/AdoMcpBridge/issues/35)) ([e83d6e4](https://github.com/a-teece/AdoMcpBridge/commit/e83d6e451e2ef09ad2900ef492932a56b1533efa))

## [0.1.3](https://github.com/a-teece/AdoMcpBridge/compare/v0.1.2...v0.1.3) (2026-06-10)


### Bug Fixes

* correlate the Entra callback by state instead of session_id ([#32](https://github.com/a-teece/AdoMcpBridge/issues/32)) ([c3f1890](https://github.com/a-teece/AdoMcpBridge/commit/c3f18906ce11b77cc36dc273d3ef1b5658a1a44d))

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
