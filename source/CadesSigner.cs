using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TagErp.EtaSigner;

/// <summary>
/// CAdES-BES signature over an ETA canonical string, as the ETA SDK describes it: SHA-256,
/// detached content, signing time and the ESS signing-certificate-v2 attribute. The private
/// key stays on the USB token; the token driver shows its own PIN prompt when the key is first
/// used by this process, so one run (one batch) asks for the PIN once.
/// </summary>
public static class CadesSigner
{
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    // ETA's reference signer wraps the canonical bytes as digested-data content.
    // TODO(5.11): confirm against the ETA pre-production environment with a real token.
    private const string DigestedDataOid = "1.2.840.113549.1.7.5";

    public static string Sign(string canonical, X509Certificate2 certificate)
    {
        var content = new ContentInfo(new Oid(DigestedDataOid), Encoding.UTF8.GetBytes(canonical));
        var cms = new SignedCms(content, detached: true);

        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = new Oid(Sha256Oid),
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
        signer.SignedAttributes.Add(SigningCertificateV2(certificate));

        cms.ComputeSignature(signer, silent: false);

        return Convert.ToBase64String(cms.Encode());
    }

    /// <summary>
    /// SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 }
    /// ESSCertIDv2 ::= SEQUENCE { hashAlgorithm (SHA-256), certHash OCTET STRING, issuerSerial }
    /// </summary>
    private static AsnEncodedData SigningCertificateV2(X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(Sha256Oid);
            }

            writer.WriteOctetString(SHA256.HashData(certificate.RawData));

            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    // GeneralName [4] directoryName
                    var directoryName = new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true);
                    using (writer.PushSequence(directoryName))
                    {
                        writer.WriteEncodedValue(certificate.IssuerName.RawData);
                    }
                }

                writer.WriteInteger(new System.Numerics.BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true));
            }
        }

        return new AsnEncodedData(new Oid(SigningCertificateV2Oid), writer.Encode());
    }
}
