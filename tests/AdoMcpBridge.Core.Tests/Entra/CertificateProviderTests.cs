using System.Security.Cryptography.X509Certificates;
using AdoMcpBridge.Core.Entra;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class CertificateProviderTests
{
    private static (CertificateClient certClient, SecretClient secretClient) Arrange(string version = "v1")
    {
        using var cert = TestCertificates.CreateSelfSigned();
        var pfx = cert.Export(X509ContentType.Pfx, "x");

        var certClient = Substitute.For<CertificateClient>();
        var secretClient = Substitute.For<SecretClient>();

        var secretId = new Uri($"https://kv.example/secrets/ado-mcp-bridge/{version}");
        var kvCert = CertificateModelFactory.KeyVaultCertificateWithPolicy(
            properties: CertificateModelFactory.CertificateProperties(
                name: "ado-mcp-bridge",
                vaultUri: new Uri("https://kv.example/"),
                version: version),
            secretId: secretId);
        certClient.GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(kvCert, Substitute.For<Response>()));

        var kvSecret = new KeyVaultSecret("ado-mcp-bridge", Convert.ToBase64String(pfx));
        secretClient.GetSecretAsync("ado-mcp-bridge", version, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(kvSecret, Substitute.For<Response>()));

        return (certClient, secretClient);
    }

    private static IOptions<EntraOptions> Opts() => Options.Create(new EntraOptions
    {
        TenantId = "tid",
        ClientId = "cid",
        CertificateName = "ado-mcp-bridge",
        KeyVaultUri = "https://kv.example/",
        Authority = "https://login.microsoftonline.com/tid/v2.0",
        PfxPassword = "x",
    });

    [Fact]
    public async Task First_call_fetches_from_key_vault()
    {
        var (cc, sc) = Arrange();
        using var sut = new CertificateProvider(cc, sc, Opts());

        var cert = await sut.GetCertificateAsync(CancellationToken.None);

        cert.HasPrivateKey.Should().BeTrue();
        await cc.Received(1).GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>());
        await sc.Received(1).GetSecretAsync("ado-mcp-bridge", "v1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Second_call_uses_cached_when_version_unchanged()
    {
        var (cc, sc) = Arrange();
        using var sut = new CertificateProvider(cc, sc, Opts());

        _ = await sut.GetCertificateAsync(CancellationToken.None);
        _ = await sut.GetCertificateAsync(CancellationToken.None);

        await cc.Received(2).GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>());
        await sc.Received(1).GetSecretAsync("ado-mcp-bridge", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refetches_when_version_changes()
    {
        using var cert = TestCertificates.CreateSelfSigned();
        var pfx = Convert.ToBase64String(cert.Export(X509ContentType.Pfx, "x"));

        var cc = Substitute.For<CertificateClient>();
        var sc = Substitute.For<SecretClient>();

        // Metadata returns v1 on the first call, then v2 on subsequent calls.
        cc.GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>())
          .Returns(
              _ => Response.FromValue(MetaForVersion("v1"), Substitute.For<Response>()),
              _ => Response.FromValue(MetaForVersion("v2"), Substitute.For<Response>()));

        sc.GetSecretAsync("ado-mcp-bridge", "v1", Arg.Any<CancellationToken>())
          .Returns(Response.FromValue(new KeyVaultSecret("ado-mcp-bridge", pfx), Substitute.For<Response>()));
        sc.GetSecretAsync("ado-mcp-bridge", "v2", Arg.Any<CancellationToken>())
          .Returns(Response.FromValue(new KeyVaultSecret("ado-mcp-bridge", pfx), Substitute.For<Response>()));

        using var sut = new CertificateProvider(cc, sc, Opts());
        _ = await sut.GetCertificateAsync(CancellationToken.None);
        _ = await sut.GetCertificateAsync(CancellationToken.None);

        await sc.Received(1).GetSecretAsync("ado-mcp-bridge", "v1", Arg.Any<CancellationToken>());
        await sc.Received(1).GetSecretAsync("ado-mcp-bridge", "v2", Arg.Any<CancellationToken>());
    }

    private static KeyVaultCertificateWithPolicy MetaForVersion(string version) =>
        CertificateModelFactory.KeyVaultCertificateWithPolicy(
            properties: CertificateModelFactory.CertificateProperties(
                name: "ado-mcp-bridge",
                vaultUri: new Uri("https://kv.example/"),
                version: version),
            secretId: new Uri($"https://kv.example/secrets/ado-mcp-bridge/{version}"));

    [Fact]
    public async Task Wraps_key_vault_failure_in_EntraAuthException()
    {
        var cc = Substitute.For<CertificateClient>();
        var sc = Substitute.For<SecretClient>();
        cc.GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>())
          .Returns<Response<KeyVaultCertificateWithPolicy>>(_ => throw new RequestFailedException(403, "denied"));

        using var sut = new CertificateProvider(cc, sc, Opts());

        var act = async () => await sut.GetCertificateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.CertificateUnavailable);
    }
}
