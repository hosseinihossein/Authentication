using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AspApp.Helpers;

public class CertManager
{
    public string CertsDirPath { get; }

    private string EcdsaSigningCertPath { get; }
    private string EcdsaKeyEncryptionCertPath { get; }

    private string RsaSigningCertPath { get; }
    private string RsaKeyEncryptionCertPath { get; }


    public CertManager(IWebHostEnvironment hostEnv)
    {
        CertsDirPath = Path.Combine(hostEnv.ContentRootPath, "Certs");
        Directory.CreateDirectory(CertsDirPath);

        EcdsaSigningCertPath = Path.Combine(CertsDirPath, "EcdsaSigningCert.pfx");
        EcdsaKeyEncryptionCertPath = Path.Combine(CertsDirPath, "EcdsaKeyEncryptionCert.pfx");

        RsaSigningCertPath = Path.Combine(CertsDirPath, "RsaSigningCert.pfx");
        RsaKeyEncryptionCertPath = Path.Combine(CertsDirPath, "RsaKeyEncryptionCert.pfx");
    }


    // **********    Ecdsa    **********
    public async Task<X509Certificate2> GetEcdsaSigningCert()
    {
        if (!File.Exists(EcdsaSigningCertPath))
        {
            // Create and save the Ecdsa signing cert
            X509Certificate2 newCert = GenerateEcdsaSigningCert();
            await File.WriteAllBytesAsync(
                EcdsaSigningCertPath,
                newCert.Export(X509ContentType.Pfx, string.Empty)
            );
            return newCert;
        }

        using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(
            EcdsaSigningCertPath,
            string.Empty,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet
        );

        return cert;
    }
    private X509Certificate2 GenerateEcdsaSigningCert()
    {
        // 1. Create an ECDSA key pair
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // 2. Define certificate subject
        var subject = new X500DistinguishedName("CN=EcdsaSigningCertificate");

        // 3. Create certificate request
        var request = new CertificateRequest(
            subject,
            ecdsa,
            HashAlgorithmName.SHA256
        );

        // Optional: Add extensions
        //request.CertificateExtensions.Add(
        //    new X509BasicConstraintsExtension(false, false, 0, false)
        //);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,// | X509KeyUsageFlags.KeyEncipherment,
                critical: true
            )
        );
        //request.CertificateExtensions.Add(
        //    new X509SubjectKeyIdentifierExtension(request.PublicKey, false)
        //);

        using X509Certificate2 ecdsaSigningCert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2)
        );

        return ecdsaSigningCert;
    }

    public async Task<X509Certificate2> GetEcdsaKeyEncryptionCert()
    {
        if (!File.Exists(EcdsaKeyEncryptionCertPath))
        {
            // Create and save the Ecdsa KeyEncryption cert
            X509Certificate2 newCert = GenerateEcdsaKeyEncryptionCert();
            await File.WriteAllBytesAsync(
                EcdsaKeyEncryptionCertPath,
                newCert.Export(X509ContentType.Pfx, string.Empty)
            );
            return newCert;
        }

        using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(
            EcdsaKeyEncryptionCertPath,
            string.Empty,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet
        );

        return cert;
    }
    private X509Certificate2 GenerateEcdsaKeyEncryptionCert()
    {
        // 1. Create an ECDSA key pair
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // 2. Define certificate subject
        var subject = new X500DistinguishedName("CN=EcdsaKeyEncryptionCertificate");

        // 3. Create certificate request
        var request = new CertificateRequest(
            subject,
            ecdsa,
            HashAlgorithmName.SHA256
        );

        // Optional: Add extensions
        //request.CertificateExtensions.Add(
        //    new X509BasicConstraintsExtension(false, false, 0, false)
        //);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyEncipherment,
                critical: true
            )
        );
        //request.CertificateExtensions.Add(
        //    new X509SubjectKeyIdentifierExtension(request.PublicKey, false)
        //);

        using X509Certificate2 ecdsaKeyEncryptionCert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2)
        );

        return ecdsaKeyEncryptionCert;
    }



    // **********    Rsa    **********
    public async Task<X509Certificate2> GetRsaSigningCert()
    {
        if (!File.Exists(RsaSigningCertPath))
        {
            // Create and save the Rsa signing cert
            X509Certificate2 newCert = GenerateRsaSigningCert();
            await File.WriteAllBytesAsync(
                RsaSigningCertPath,
                newCert.Export(X509ContentType.Pfx, string.Empty)
            );
            return newCert;
        }

        using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(
            RsaSigningCertPath,
            string.Empty,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet
        );

        return cert;
    }
    private X509Certificate2 GenerateRsaSigningCert()
    {
        using var algorithm = RSA.Create(keySizeInBits: 4096);

        var subject = new X500DistinguishedName("CN=RsaSigningCertificate");
        var request = new CertificateRequest(
            subject,
            algorithm, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true
            )
        );

        using X509Certificate2 rsaSigningCert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2)
        );

        return rsaSigningCert;
    }

    public async Task<X509Certificate2> GetRsaKeyEncryptionCert()
    {
        if (!File.Exists(RsaKeyEncryptionCertPath))
        {
            // Create and save the Rsa encryption cert
            X509Certificate2 newCert = GenerateRsaKeyEncryptionCert();
            await File.WriteAllBytesAsync(
                RsaKeyEncryptionCertPath,
                newCert.Export(X509ContentType.Pfx, string.Empty)
            );
            return newCert;
        }

        using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(
            RsaKeyEncryptionCertPath,
            string.Empty,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet
        );

        return cert;
    }
    private X509Certificate2 GenerateRsaKeyEncryptionCert()
    {
        using var algorithm = RSA.Create(keySizeInBits: 4096);

        var subject = new X500DistinguishedName("CN=RsaEncryptionCertificate");
        var request = new CertificateRequest(
            subject,
            algorithm, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyEncipherment,
                critical: true
            )
        );

        using X509Certificate2 rsaKeyEncryptionCert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2)
        );

        return rsaKeyEncryptionCert;
    }



}