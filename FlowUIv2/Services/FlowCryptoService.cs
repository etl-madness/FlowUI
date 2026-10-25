using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FlowUIv2.Models;

namespace FlowUIv2.Services;

/// <summary>
/// Implements wire-compatible cryptographic primitives and envelopes matching go-flow/pkg/flowcrypto:
/// - FLOWENC:v1:&lt;base64(salt[16] || nonce[12] || ciphertext || tag[16])&gt; (AES-256-GCM + PBKDF2-HMAC-SHA256, 100k iterations)
/// - FLOWSIGNED:v1:&lt;format&gt;:&lt;base64(sig)&gt;:&lt;base64(content)&gt;
/// - FLOWSIG:v1:&lt;format&gt;:&lt;base64(sig)&gt;
/// </summary>
public sealed class FlowCryptoService
{
    public const string ArmoredPrefixV1 = "FLOWENC:v1:";
    public const string SignaturePrefixV1 = "FLOWSIG:v1:";
    public const string SignedPrefixV1 = "FLOWSIGNED:v1:";

    public const string EncryptedPrefix = ArmoredPrefixV1;
    public const string SignaturePrefix = SignaturePrefixV1;
    public const string SignedPrefix = SignedPrefixV1;

    public const int DefaultKeyLength = 32;
    public const int SaltLength = 16;
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int Pbkdf2Iterations = 100_000;

    /// <summary>
    /// Generates a cryptographically secure random key formatted as Base64 (matches flowcrypto.GenerateSecureKey).
    /// </summary>
    public string GenerateSecureKey(int byteLength = DefaultKeyLength)
    {
        if (byteLength <= 0)
            byteLength = DefaultKeyLength;

        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes);
    }

    public bool IsEncryptedPayload(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return content.TrimStart().StartsWith(ArmoredPrefixV1, StringComparison.Ordinal);
    }

    public bool IsEncryptedContent(string? content) => IsEncryptedPayload(content);

    public bool IsSignedPayload(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return content.TrimStart().StartsWith(SignedPrefixV1, StringComparison.Ordinal);
    }

    public bool IsSignedEnvelope(string? content) => IsSignedPayload(content);

    public bool IsDetachedSignature(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return content.TrimStart().StartsWith(SignaturePrefixV1, StringComparison.Ordinal);
    }

    /// <summary>
    /// Inspects an artifact string for FLOWENC:v1:, FLOWSIGNED:v1:, or FLOWSIG:v1: envelopes without requiring keys.
    /// If outer layer is FLOWSIGNED:v1:, unwraps the base64 content to check if inner payload is FLOWENC:v1: (Encrypt-then-Sign).
    /// </summary>
    public EnvelopeInspectionResult InspectEnvelope(string? rawContent)
    {
        var result = new EnvelopeInspectionResult
        {
            UnwrappedContent = rawContent ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(rawContent))
            return result;

        var trimmed = rawContent.Trim();

        if (trimmed.StartsWith(SignaturePrefixV1, StringComparison.Ordinal))
        {
            result.IsDetachedSignature = true;
            var parts = trimmed.Split(':', 4);
            if (parts.Length >= 4)
            {
                result.SignatureFormat = parts[2];
                result.SignatureBase64 = parts[3];
            }
            return result;
        }

        if (trimmed.StartsWith(SignedPrefixV1, StringComparison.Ordinal))
        {
            result.IsSigned = true;
            if (TryParseSignedPayload(trimmed, out var fmt, out var sigBytes, out var innerBytes))
            {
                result.SignatureFormat = fmt;
                result.SignatureBase64 = Convert.ToBase64String(sigBytes);
                var innerStr = Encoding.UTF8.GetString(innerBytes);
                result.UnwrappedContent = innerStr;
                if (IsEncryptedPayload(innerStr))
                {
                    result.IsEncrypted = true;
                    result.CompositionOrder = "encrypt-then-sign";
                }
                else
                {
                    result.CompositionOrder = "signed";
                }
            }
            return result;
        }

        if (trimmed.StartsWith(ArmoredPrefixV1, StringComparison.Ordinal))
        {
            result.IsEncrypted = true;
            result.CompositionOrder = "encrypted";
        }

        return result;
    }

    /// <summary>
    /// Encrypts plaintext into a FLOWENC:v1:&lt;base64&gt; envelope matching go-flow/pkg/flowcrypto.EncryptArmored.
    /// </summary>
    public string EncryptArmored(string plaintext, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Secure key / passphrase cannot be empty.", nameof(passphrase));

        var plainBytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
        var packed = EncryptBytes(plainBytes, passphrase.Trim());
        return ArmoredPrefixV1 + Convert.ToBase64String(packed);
    }

    /// <summary>
    /// Decrypts a FLOWENC:v1:&lt;base64&gt; envelope matching go-flow/pkg/flowcrypto.DecryptArmored.
    /// </summary>
    public string DecryptArmored(string armoredText, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(armoredText))
            throw new ArgumentException("Encrypted payload is empty.", nameof(armoredText));
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Secure key / passphrase is required to decrypt.", nameof(passphrase));

        var trimmed = armoredText.Trim();
        if (!trimmed.StartsWith(ArmoredPrefixV1, StringComparison.Ordinal))
            throw new InvalidOperationException("Data is not in recognized FLOWENC:v1: encrypted format.");

        var b64 = trimmed[ArmoredPrefixV1.Length..].Trim();
        var packed = Convert.FromBase64String(b64);
        var plainBytes = DecryptBytes(packed, passphrase.Trim());
        return Encoding.UTF8.GetString(plainBytes);
    }

    public string EncryptPayload(string plaintext, string passphrase) => EncryptArmored(plaintext, passphrase);
    public string EncryptContent(string plaintext, string passphrase) => EncryptArmored(plaintext, passphrase);
    public string DecryptPayload(string armoredText, string passphrase) => DecryptArmored(armoredText, passphrase);
    public string DecryptContent(string armoredText, string passphrase) => DecryptArmored(armoredText, passphrase);

    public byte[] EncryptBytes(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            DefaultKeyLength);

        try
        {
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagLength];

            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            // Layout: salt (16B) || nonce (12B) || ciphertext || tag (16B)
            var output = new byte[SaltLength + NonceLength + ciphertext.Length + TagLength];
            Buffer.BlockCopy(salt, 0, output, 0, SaltLength);
            Buffer.BlockCopy(nonce, 0, output, SaltLength, NonceLength);
            Buffer.BlockCopy(ciphertext, 0, output, SaltLength + NonceLength, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, output, SaltLength + NonceLength + ciphertext.Length, TagLength);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] DecryptBytes(byte[] packedPayload, string passphrase)
    {
        int minLength = SaltLength + NonceLength + TagLength;
        if (packedPayload.Length < minLength)
            throw new InvalidOperationException("Encrypted payload is too short or corrupted.");

        var salt = packedPayload.AsSpan(0, SaltLength);
        var nonce = packedPayload.AsSpan(SaltLength, NonceLength);
        int cipherLen = packedPayload.Length - minLength;
        var ciphertext = packedPayload.AsSpan(SaltLength + NonceLength, cipherLen);
        var tag = packedPayload.AsSpan(SaltLength + NonceLength + cipherLen, TagLength);

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            DefaultKeyLength);

        try
        {
            var plaintext = new byte[cipherLen];
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Decryption failed: invalid secure key or tampered ciphertext.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string WrapSignatureOnly(string format, byte[] sigBytes) =>
        $"{SignaturePrefixV1}{format}:{Convert.ToBase64String(sigBytes)}";

    public static string WrapSignedPayload(string format, byte[] sigBytes, byte[] contentBytes) =>
        $"{SignedPrefixV1}{format}:{Convert.ToBase64String(sigBytes)}:{Convert.ToBase64String(contentBytes)}";

    public static bool TryParseSignedPayload(string signedEnvelope, out string format, out byte[] sigBytes, out byte[] contentBytes)
    {
        format = string.Empty;
        sigBytes = Array.Empty<byte>();
        contentBytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(signedEnvelope))
            return false;

        var trimmed = signedEnvelope.Trim();
        if (!trimmed.StartsWith(SignedPrefixV1, StringComparison.Ordinal))
            return false;

        var parts = trimmed.Split(':', 5);
        if (parts.Length < 5)
            return false;

        try
        {
            format = parts[2];
            sigBytes = Convert.FromBase64String(parts[3]);
            contentBytes = Convert.FromBase64String(parts[4]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryUnwrapSignedPayload(string signedEnvelope, out string format, out string innerText, out byte[] sigBytes)
    {
        innerText = string.Empty;
        if (TryParseSignedPayload(signedEnvelope, out format, out sigBytes, out var contentBytes))
        {
            innerText = Encoding.UTF8.GetString(contentBytes);
            return true;
        }
        return false;
    }

    public static bool TryUnwrapSignatureOnly(string? detachedSig, out string format, out byte[] sigBytes)
    {
        format = string.Empty;
        sigBytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(detachedSig))
            return false;

        var trimmed = detachedSig.Trim();
        if (!trimmed.StartsWith(SignaturePrefixV1, StringComparison.Ordinal))
            return false;

        var parts = trimmed.Split(':', 4);
        if (parts.Length < 4)
            return false;

        try
        {
            format = parts[2];
            sigBytes = Convert.FromBase64String(parts[3]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public CryptoKeygenResponse GenerateKeyPairLocal(string algorithm, string commonName) =>
        GenerateKeyPairLocal(new CryptoKeygenRequest
        {
            Algorithm = algorithm,
            CommonName = commonName,
            GenerateCert = true,
            ValidityDays = 365
        });

    public (string PrivateKeyPem, string PublicKeyPem) GenerateRsaKeyPairLocal(int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        return (rsa.ExportRSAPrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    public (string PrivateKeyPem, string PublicKeyPem) GenerateEcdsaKeyPairLocal(string curveName = "P256")
    {
        var curve = curveName.Contains("384", StringComparison.OrdinalIgnoreCase)
            ? ECCurve.NamedCurves.nistP384
            : ECCurve.NamedCurves.nistP256;
        using var ecdsa = ECDsa.Create(curve);
        return (ecdsa.ExportECPrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    public (string PrivateKeyPem, string CertificatePem, string PublicKeyPem) GenerateSelfSignedCertLocal(
        string commonName,
        string organization = "Flow Studio",
        int validDays = 365)
    {
        var cn = string.IsNullOrWhiteSpace(commonName) ? "Flow Pipeline Signer" : commonName.Trim();
        var org = string.IsNullOrWhiteSpace(organization) ? "Flow Studio" : organization.Trim();
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privPem = ecdsa.ExportECPrivateKeyPem();
        var pubPem = ecdsa.ExportSubjectPublicKeyInfoPem();

        var certReq = new CertificateRequest($"CN={cn}, O={org}", ecdsa, HashAlgorithmName.SHA256);
        certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, false));
        certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false));

        using var cert = certReq.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddDays(validDays > 0 ? validDays : 365));
        return (privPem, cert.ExportCertificatePem(), pubPem);
    }

    public byte[] SignOpenSslLocal(byte[] dataToSign, string privateKeyPem, string? passphrase = null)
    {
        var pem = privateKeyPem.Trim();
        if (pem.Contains("EC PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            using var ecdsa = ECDsa.Create();
            if (!string.IsNullOrWhiteSpace(passphrase) && pem.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase))
                ecdsa.ImportFromEncryptedPem(pem, passphrase);
            else
                ecdsa.ImportFromPem(pem);
            return ecdsa.SignData(dataToSign, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        else
        {
            using var rsa = RSA.Create();
            if (!string.IsNullOrWhiteSpace(passphrase) && pem.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase))
                rsa.ImportFromEncryptedPem(pem, passphrase);
            else
                rsa.ImportFromPem(pem);
            return rsa.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    public bool VerifyOpenSslLocal(byte[] dataBytes, byte[] sigBytes, string? publicKeyOrCertPem)
    {
        if (string.IsNullOrWhiteSpace(publicKeyOrCertPem))
            return false;

        var keyOrCertPem = publicKeyOrCertPem.Trim();
        if (keyOrCertPem.Contains("BEGIN CERTIFICATE", StringComparison.OrdinalIgnoreCase))
        {
            using var cert = X509Certificate2.CreateFromPem(keyOrCertPem);
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is not null)
                return rsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa is not null)
                return ecdsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(keyOrCertPem);
            return rsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(keyOrCertPem);
            return ecdsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
    }

    /// <summary>
    /// Native .NET fallback for generating RSA or ECDSA PEM keypairs and self-signed code-signing X.509 certificates.
    /// </summary>
    public CryptoKeygenResponse GenerateKeyPairLocal(CryptoKeygenRequest req)
    {
        var alg = (req.Algorithm ?? "RSA-2048").Trim().ToUpperInvariant();
        if (alg is "AES" or "AES-256" or "AES-256-GCM" or "SECURE-KEY")
        {
            return new CryptoKeygenResponse
            {
                Algorithm = "AES-256-GCM",
                SecureKey = GenerateSecureKey(req.ByteLength)
            };
        }

        var cn = string.IsNullOrWhiteSpace(req.CommonName) ? "Flow Pipeline Signer" : req.CommonName.Trim();
        int days = req.ValidityDays > 0 ? req.ValidityDays : 365;

        if (alg.StartsWith("ECDSA") || alg.StartsWith("EC-") || alg.StartsWith("P256") || alg.StartsWith("P384"))
        {
            var curve = alg.Contains("384") ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256;
            using var ecdsa = ECDsa.Create(curve);
            var privPem = ecdsa.ExportECPrivateKeyPem();
            var pubPem = ecdsa.ExportSubjectPublicKeyInfoPem();
            var certPem = string.Empty;

            if (req.GenerateCert)
            {
                var certReq = new CertificateRequest($"CN={cn}, O=Flow", ecdsa, HashAlgorithmName.SHA256);
                certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, false));
                certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false)); // Code Signing

                using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(days));
                certPem = cert.ExportCertificatePem();
            }

            var fpBytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrEmpty(certPem) ? pubPem : certPem));
            return new CryptoKeygenResponse
            {
                Algorithm = alg.Contains("384") ? "ECDSA-P384" : "ECDSA-P256",
                PrivateKeyPem = privPem,
                PublicKeyPem = pubPem,
                CertificatePem = certPem,
                Fingerprint = Convert.ToHexString(fpBytes[..16])
            };
        }
        else
        {
            int bits = alg.Contains("4096") ? 4096 : 2048;
            using var rsa = RSA.Create(bits);
            var privPem = rsa.ExportRSAPrivateKeyPem();
            var pubPem = rsa.ExportSubjectPublicKeyInfoPem();
            var certPem = string.Empty;

            if (req.GenerateCert)
            {
                var certReq = new CertificateRequest($"CN={cn}, O=Flow", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyCertSign, false));
                certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false)); // Code Signing

                using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(days));
                certPem = cert.ExportCertificatePem();
            }

            var fpBytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrEmpty(certPem) ? pubPem : certPem));
            return new CryptoKeygenResponse
            {
                Algorithm = $"RSA-{bits}",
                PrivateKeyPem = privPem,
                PublicKeyPem = pubPem,
                CertificatePem = certPem,
                Fingerprint = Convert.ToHexString(fpBytes[..16])
            };
        }
    }

    /// <summary>
    /// Native .NET OpenSSL-compatible signing fallback for RSA and ECDSA PEM private keys.
    /// </summary>
    public CryptoSignResponse SignLocalOpenSsl(CryptoSignRequest req)
    {
        var dataToSign = Encoding.UTF8.GetBytes(req.Content);
        var encMode = (req.EncryptMode ?? "none").Trim().ToLowerInvariant();

        if (encMode == "encrypt-then-sign" && !IsEncryptedPayload(req.Content))
        {
            var encryptedInner = EncryptArmored(req.Content, req.SecureKey);
            dataToSign = Encoding.UTF8.GetBytes(encryptedInner);
        }

        byte[] sigBytes;
        string sigFormat;

        var pem = req.PrivateKeyPem.Trim();
        if (pem.Contains("EC PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            sigBytes = ecdsa.SignData(dataToSign, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            sigFormat = "OPENSSL-ECDSA-SHA256";
        }
        else
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            sigBytes = rsa.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            sigFormat = "OPENSSL-RSA-SHA256";
        }

        var detached = WrapSignatureOnly(sigFormat, sigBytes);
        var wrapped = WrapSignedPayload(sigFormat, sigBytes, dataToSign);
        var finalPayload = req.Wrap ? wrapped : Encoding.UTF8.GetString(dataToSign);
        bool isEncrypted = encMode == "encrypt-then-sign" || IsEncryptedPayload(req.Content);

        if (encMode == "sign-then-encrypt")
        {
            finalPayload = EncryptArmored(wrapped, req.SecureKey);
            isEncrypted = true;
        }

        return new CryptoSignResponse
        {
            Format = sigFormat,
            DetachedSignature = detached,
            SignedPayload = finalPayload,
            Encrypted = isEncrypted,
            Signed = true
        };
    }

    /// <summary>
    /// Native .NET OpenSSL-compatible verification fallback for RSA and ECDSA PEM public keys / X.509 certificates.
    /// </summary>
    public CryptoVerifyResponse VerifyLocalOpenSsl(CryptoVerifyRequest req)
    {
        var raw = (req.Content ?? string.Empty).Trim();
        bool wasEncrypted = IsEncryptedPayload(raw);

        if (wasEncrypted && !string.IsNullOrWhiteSpace(req.SecureKey))
        {
            raw = DecryptArmored(raw, req.SecureKey);
        }

        byte[] dataBytes;
        byte[] sigBytes;
        string format = "OPENSSL";

        if (IsSignedPayload(raw))
        {
            if (!TryParseSignedPayload(raw, out format, out sigBytes, out dataBytes))
            {
                return new CryptoVerifyResponse
                {
                    Valid = false,
                    Signed = true,
                    Encrypted = wasEncrypted,
                    ErrorMessage = "Malformed FLOWSIGNED:v1: envelope."
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(req.Signature))
        {
            dataBytes = Encoding.UTF8.GetBytes(raw);
            var trimmedSig = req.Signature.Trim();
            if (trimmedSig.StartsWith(SignaturePrefixV1, StringComparison.Ordinal))
            {
                var parts = trimmedSig.Split(':', 4);
                format = parts.Length >= 3 ? parts[2] : "OPENSSL";
                sigBytes = Convert.FromBase64String(parts[^1]);
            }
            else
            {
                sigBytes = Convert.FromBase64String(trimmedSig);
            }
        }
        else
        {
            return new CryptoVerifyResponse
            {
                Valid = false,
                Signed = false,
                Encrypted = wasEncrypted,
                UnwrappedContent = raw,
                ErrorMessage = "Payload is not signed."
            };
        }

        var unwrappedStr = Encoding.UTF8.GetString(dataBytes);
        if (IsEncryptedPayload(unwrappedStr))
        {
            wasEncrypted = true;
            if (!string.IsNullOrWhiteSpace(req.SecureKey))
            {
                try { unwrappedStr = DecryptArmored(unwrappedStr, req.SecureKey); } catch { /* keep encrypted if key wrong */ }
            }
        }

        var keyOrCertPem = !string.IsNullOrWhiteSpace(req.CertPem) ? req.CertPem.Trim() : req.PublicKeyPem.Trim();
        if (string.IsNullOrWhiteSpace(keyOrCertPem))
        {
            return new CryptoVerifyResponse
            {
                Valid = false,
                Format = format,
                Signed = true,
                Encrypted = wasEncrypted,
                UnwrappedContent = unwrappedStr,
                ErrorMessage = "Signed envelope unwrapped, but no public key or X.509 certificate was provided for verification."
            };
        }

        X509Certificate2? cert = null;
        bool valid = false;
        string alg = format;
        string signerInfo = "Public Key";

        try
        {
            if (keyOrCertPem.Contains("BEGIN CERTIFICATE", StringComparison.OrdinalIgnoreCase))
            {
                cert = X509Certificate2.CreateFromPem(keyOrCertPem);
                signerInfo = cert.GetNameInfo(X509NameType.SimpleName, false);
                using var rsa = cert.GetRSAPublicKey();
                if (rsa is not null)
                {
                    valid = rsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    alg = "RSA-SHA256";
                }
                else
                {
                    using var ecdsa = cert.GetECDsaPublicKey();
                    if (ecdsa is not null)
                    {
                        valid = ecdsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                        alg = "ECDSA-SHA256";
                    }
                }
            }
            else
            {
                try
                {
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(keyOrCertPem);
                    valid = rsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    alg = "RSA-SHA256";
                }
                catch
                {
                    using var ecdsa = ECDsa.Create();
                    ecdsa.ImportFromPem(keyOrCertPem);
                    valid = ecdsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                    alg = "ECDSA-SHA256";
                }
            }
        }
        catch (Exception ex)
        {
            return new CryptoVerifyResponse
            {
                Valid = false,
                Format = format,
                Signed = true,
                Encrypted = wasEncrypted,
                UnwrappedContent = unwrappedStr,
                ErrorMessage = ex.Message
            };
        }

        var resp = new CryptoVerifyResponse
        {
            Valid = valid,
            Format = format,
            Algorithm = alg,
            SignerInfo = signerInfo,
            Signed = true,
            Encrypted = wasEncrypted,
            UnwrappedContent = unwrappedStr,
            ErrorMessage = valid ? string.Empty : "Cryptographic signature verification failed."
        };

        if (cert is not null)
        {
            resp.Subject = cert.Subject;
            resp.Issuer = cert.Issuer;
            resp.SerialNumber = cert.SerialNumber;
            resp.NotBefore = cert.NotBefore.ToUniversalTime().ToString("O");
            resp.NotAfter = cert.NotAfter.ToUniversalTime().ToString("O");
            resp.Fingerprint = cert.Thumbprint;
            cert.Dispose();
        }

        return resp;
    }
}
