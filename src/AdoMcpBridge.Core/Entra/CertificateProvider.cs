using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.Entra;

public sealed class CertificateProvider : ICertificateProvider, IDisposable
{
    private readonly CertificateClient _certClient;
    private readonly SecretClient _secretClient;
    private readonly EntraOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedVersion;
    private X509Certificate2? _cached;

    public CertificateProvider(
        CertificateClient certClient,
        SecretClient secretClient,
        IOptions<EntraOptions> options)
    {
        _certClient = certClient;
        _secretClient = secretClient;
        _options = options.Value;
    }

    public async ValueTask<X509Certificate2> GetCertificateAsync(CancellationToken ct)
    {
        KeyVaultCertificateWithPolicy meta;
        try
        {
            meta = await _certClient.GetCertificateAsync(_options.CertificateName, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw new EntraAuthException(
                EntraAuthFailure.CertificateUnavailable,
                ex.Status,
                ex.ErrorCode,
                $"Failed to fetch certificate metadata '{_options.CertificateName}' from Key Vault.",
                ex);
        }

        var version = meta.Properties.Version;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null && _cachedVersion == version)
            {
                return _cached;
            }

            KeyVaultSecret secret;
            try
            {
                secret = await _secretClient
                    .GetSecretAsync(_options.CertificateName, version, ct)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex)
            {
                throw new EntraAuthException(
                    EntraAuthFailure.CertificateUnavailable,
                    ex.Status,
                    ex.ErrorCode,
                    $"Failed to fetch certificate secret '{_options.CertificateName}' version '{version}' from Key Vault.",
                    ex);
            }

            var pfxBytes = Convert.FromBase64String(secret.Value);
            var loaded = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                _options.PfxPassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

            _cached?.Dispose();
            _cached = loaded;
            _cachedVersion = version;
            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _cached?.Dispose();
        _gate.Dispose();
    }
}
