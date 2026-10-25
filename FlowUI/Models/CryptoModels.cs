using System.Text.Json.Serialization;

namespace FlowUI.Models;

public sealed class CryptoKeygenRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "RSA-2048";

    [JsonPropertyName("bits")]
    public int Bits { get; set; } = 2048;

    [JsonPropertyName("curve")]
    public string Curve { get; set; } = "P256";

    [JsonPropertyName("common_name")]
    public string CommonName { get; set; } = "Flow Pipeline Signer";

    [JsonPropertyName("organization")]
    public string Organization { get; set; } = "Flow Studio";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = "signer@flow.local";

    [JsonPropertyName("validity_days")]
    public int ValidityDays { get; set; } = 365;

    [JsonPropertyName("valid_days")]
    public int ValidDays
    {
        get => ValidityDays;
        set => ValidityDays = value;
    }

    [JsonPropertyName("generate_cert")]
    public bool GenerateCert { get; set; } = true;

    [JsonPropertyName("byte_length")]
    public int ByteLength { get; set; } = 32;
}

public sealed class CryptoKeygenResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("format")]
    public string Format { get; set; } = "openssl";

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("secure_key")]
    public string SecureKey { get; set; } = string.Empty;

    [JsonPropertyName("private_key_pem")]
    public string PrivateKeyPem { get; set; } = string.Empty;

    [JsonPropertyName("public_key_pem")]
    public string PublicKeyPem { get; set; } = string.Empty;

    [JsonPropertyName("certificate_pem")]
    public string CertificatePem { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
}

public sealed class CryptoSignRequest
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload
    {
        get => Content;
        set => Content = value ?? string.Empty;
    }

    [JsonPropertyName("scheme")]
    public string Scheme { get; set; } = "OPENSSL"; // OPENSSL, PKCS7, PGP

    [JsonPropertyName("format")]
    public string Format
    {
        get => Scheme;
        set => Scheme = value ?? "openssl";
    }

    [JsonPropertyName("private_key_pem")]
    public string PrivateKeyPem { get; set; } = string.Empty;

    [JsonPropertyName("cert_pem")]
    public string CertPem { get; set; } = string.Empty;

    [JsonPropertyName("passphrase")]
    public string Passphrase { get; set; } = string.Empty;

    [JsonPropertyName("wrap")]
    public bool Wrap { get; set; } = true;

    [JsonPropertyName("encrypt_mode")]
    public string EncryptMode { get; set; } = "none"; // "none", "sign-then-encrypt", "encrypt-then-sign"

    [JsonPropertyName("secure_key")]
    public string SecureKey { get; set; } = string.Empty;
}

public sealed class CryptoSignResponse
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("raw_signature_base64")]
    public string RawSignatureBase64 { get; set; } = string.Empty;

    [JsonPropertyName("detached_signature")]
    public string DetachedSignature { get; set; } = string.Empty;

    [JsonPropertyName("signed_payload")]
    public string SignedPayload { get; set; } = string.Empty;

    [JsonPropertyName("signed_envelope")]
    public string SignedEnvelope
    {
        get => SignedPayload;
        set => SignedPayload = value ?? string.Empty;
    }

    [JsonPropertyName("encrypted")]
    public bool Encrypted
    {
        get; set;
    }

    [JsonPropertyName("signed")]
    public bool Signed
    {
        get; set;
    }
}

public sealed class CryptoVerifyRequest
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload
    {
        get => Content;
        set => Content = value ?? string.Empty;
    }

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("public_key_pem")]
    public string PublicKeyPem { get; set; } = string.Empty;

    [JsonPropertyName("cert_pem")]
    public string CertPem { get; set; } = string.Empty;

    [JsonPropertyName("ca_cert_pem")]
    public string CaCertPem { get; set; } = string.Empty;

    [JsonPropertyName("keyring_pem")]
    public string KeyringPem { get; set; } = string.Empty;

    [JsonPropertyName("keyring_asc")]
    public string KeyringAsc
    {
        get => KeyringPem;
        set => KeyringPem = value ?? string.Empty;
    }

    [JsonPropertyName("secure_key")]
    public string SecureKey { get; set; } = string.Empty;
}

public sealed class CryptoVerifyResponse
{
    [JsonPropertyName("valid")]
    public bool Valid
    {
        get; set;
    }

    [JsonPropertyName("verified")]
    public bool Verified
    {
        get => Valid;
        set => Valid = value;
    }

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("signer_info")]
    public string SignerInfo { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("serial_number")]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyName("not_before")]
    public string NotBefore { get; set; } = string.Empty;

    [JsonPropertyName("not_after")]
    public string NotAfter { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("encrypted")]
    public bool Encrypted
    {
        get; set;
    }

    [JsonPropertyName("signed")]
    public bool Signed
    {
        get; set;
    }

    [JsonPropertyName("unwrapped_content")]
    public string UnwrappedContent { get; set; } = string.Empty;

    [JsonPropertyName("unwrapped_payload")]
    public string UnwrappedPayload
    {
        get => UnwrappedContent;
        set => UnwrappedContent = value ?? string.Empty;
    }

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string? Error
    {
        get => ErrorMessage;
        set => ErrorMessage = value ?? string.Empty;
    }
}

public sealed class EnvelopeInspectionResult
{
    public bool IsEncrypted
    {
        get; set;
    }
    public bool IsSigned
    {
        get; set;
    }
    public bool IsDetachedSignature
    {
        get; set;
    }
    public string SignatureFormat { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
    public string UnwrappedContent { get; set; } = string.Empty;
    public string CompositionOrder { get; set; } = "none"; // "none", "encrypted", "signed", "encrypt-then-sign", "sign-then-encrypt"
}

public sealed class SavedCryptoKeyProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Default Signer Profile";
    public string Format { get; set; } = "openssl";
    public string Algorithm { get; set; } = "RSA-2048";
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string CertificatePem { get; set; } = string.Empty;
    public string CaCertificatePem { get; set; } = string.Empty;
    public string SecureKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
