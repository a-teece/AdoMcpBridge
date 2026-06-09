using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdoMcpBridge.Core.Tests;

internal static class TestCertificates
{
    public static X509Certificate2 CreateSelfSigned(string cn = "CN=AdoMcpBridgeTest")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Re-import with exportable private key so MSAL can sign assertions.
        var pfx = cert.Export(X509ContentType.Pfx, "x");
        return X509CertificateLoader.LoadPkcs12(pfx, "x", X509KeyStorageFlags.Exportable);
    }
}
