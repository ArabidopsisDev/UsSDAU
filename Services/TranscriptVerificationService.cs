using QRCoder;
using System.Formats.Cbor;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UsSDAU.Models;

namespace UsSDAU.Services;

public sealed record TranscriptVerificationResult(
    bool IsValid,
    string Message,
    string Fingerprint,
    string StudentNo,
    int CourseCount,
    string Scope,
    DateTimeOffset IssuedAt);

public sealed class TranscriptVerificationService
{
    private const string Prefix = "USSD1:";
    private const string KeyName = "UsSDAU.Transcript.Signing.v1";

    public (string Text, byte[] QrPng, string Fingerprint) CreateSignedQr(
        AcademicSettings settings,
        StudentProfile? profile,
        IReadOnlyList<ScoreResult> results,
        bool allTerms = false)
    {
        using var key = OpenOrCreateKey();
        using var ecdsa = new ECDsaCng(key);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        var kid = SHA256.HashData(publicKey)[..16];
        var payload = BuildPayload(settings, profile, results, publicKey, allTerms);
        var protectedHeader = BuildProtectedHeader(kid);
        var signatureInput = BuildSignatureInput(protectedHeader, payload);
        var signature = ecdsa.SignData(
            signatureInput,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var cose = BuildCoseSign1(protectedHeader, payload, signature);
        var compressed = Compress(cose);
        var text = Prefix + Base45.Encode(compressed);
        var qr = PngByteQRCodeHelper.GetQRCode(text, QRCodeGenerator.ECCLevel.Q, 8, drawQuietZones: true);
        return (text, qr, Fingerprint(publicKey));
    }

    public TranscriptVerificationResult Verify(string text)
    {
        try
        {
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                return Invalid("无法识别的 UsSDAU 校验码格式。");
            var cose = DecompressBounded(Base45.Decode(text[Prefix.Length..]), 64 * 1024);
            var reader = new CborReader(cose, CborConformanceMode.Lax);
            if (reader.PeekState() == CborReaderState.Tag) reader.ReadTag();
            reader.ReadStartArray();
            var protectedHeader = reader.ReadByteString();
            reader.ReadStartMap(); reader.ReadEndMap();
            var payload = reader.ReadByteString();
            var signature = reader.ReadByteString();
            reader.ReadEndArray();

            var details = ReadPayload(payload);
            var expectedKid = SHA256.HashData(details.PublicKey)[..16];
            var encodedKid = ReadKid(protectedHeader);
            if (!CryptographicOperations.FixedTimeEquals(expectedKid, encodedKid))
                return Invalid("签名密钥标识与二维码公钥不一致。");
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(details.PublicKey, out _);
            var valid = ecdsa.VerifyData(
                BuildSignatureInput(protectedHeader, payload),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return new TranscriptVerificationResult(
                valid,
                valid ? "二维码签名有效，签名后的成绩数据未被修改。" : "签名无效，数据可能已被修改。",
                Fingerprint(details.PublicKey),
                details.StudentNo,
                details.CourseCount,
                details.Scope,
                details.IssuedAt);
        }
        catch (Exception error)
        {
            return Invalid("校验码损坏或格式不受支持：" + error.Message);
        }
    }

    private static byte[] BuildPayload(AcademicSettings settings, StudentProfile? profile, IReadOnlyList<ScoreResult> results, byte[] publicKey, bool allTerms)
    {
        var rows = results.OrderBy(x => x.Term).SelectMany(result =>
            result.Records.Select(score => (result.Term, Score: score))).ToList();
        var aggregate = ScorePolicy.Recalculate("all", rows.Select(x => x.Score), rows.Select(x => x.Score));
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(10);
        writer.WriteInt32(1); writer.WriteInt32(1);
        writer.WriteInt32(2); writer.WriteByteString(RandomNumberGenerator.GetBytes(16));
        writer.WriteInt32(3); writer.WriteInt64(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        writer.WriteInt32(4); writer.WriteTextString(Normalize(settings.Account));
        writer.WriteInt32(5); writer.WriteTextString(Normalize(profile?.Name ?? ""));
        writer.WriteInt32(6); writer.WriteTextString(allTerms || results.Count > 1 ? "all-terms" : results.FirstOrDefault()?.Term ?? settings.ScoreTerm);
        writer.WriteInt32(7);
        writer.WriteStartArray(results.Count);
        foreach (var term in results.OrderBy(x => x.Term)) writer.WriteTextString(term.Term);
        writer.WriteEndArray();
        writer.WriteInt32(8);
        writer.WriteByteString(BuildRowsDigest(rows));
        writer.WriteInt32(9);
        writer.WriteStartArray(4);
        writer.WriteInt32(rows.Count);
        writer.WriteInt32(ToFixed(aggregate.TotalCredits, 100));
        writer.WriteInt32(ToFixed(aggregate.AverageScore, 100));
        writer.WriteInt32(ToFixed(aggregate.AverageCreditGpa, 1000));
        writer.WriteEndArray();
        writer.WriteInt32(10);
        writer.WriteByteString(publicKey);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] BuildProtectedHeader(byte[] kid)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(3);
        writer.WriteInt32(1); writer.WriteInt32(-7);
        writer.WriteInt32(3); writer.WriteTextString("application/vnd.ussdau.transcript+cbor");
        writer.WriteInt32(4); writer.WriteByteString(kid);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] BuildSignatureInput(byte[] protectedHeader, byte[] payload)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartArray(4);
        writer.WriteTextString("Signature1");
        writer.WriteByteString(protectedHeader);
        writer.WriteByteString([]);
        writer.WriteByteString(payload);
        writer.WriteEndArray();
        return writer.Encode();
    }

    private static byte[] BuildCoseSign1(byte[] protectedHeader, byte[] payload, byte[] signature)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteTag((CborTag)18);
        writer.WriteStartArray(4);
        writer.WriteByteString(protectedHeader);
        writer.WriteStartMap(0); writer.WriteEndMap();
        writer.WriteByteString(payload);
        writer.WriteByteString(signature);
        writer.WriteEndArray();
        return writer.Encode();
    }

    private static (byte[] PublicKey, string StudentNo, int CourseCount, string Scope, DateTimeOffset IssuedAt) ReadPayload(byte[] payload)
    {
        byte[] publicKey = [];
        var student = "";
        var courseCount = 0;
        var scope = "";
        var issuedAt = DateTimeOffset.UnixEpoch;
        var reader = new CborReader(payload, CborConformanceMode.Lax);
        var length = reader.ReadStartMap() ?? 0;
        for (var i = 0; i < length; i++)
        {
            switch (reader.ReadInt32())
            {
                case 3: issuedAt = DateTimeOffset.FromUnixTimeSeconds(reader.ReadInt64()); break;
                case 4: student = reader.ReadTextString(); break;
                case 6: scope = reader.ReadTextString(); break;
                case 8: reader.ReadByteString(); break;
                case 9:
                    var summaryLength = reader.ReadStartArray() ?? 0;
                    if (summaryLength > 0) courseCount = reader.ReadInt32();
                    for (var item = 1; item < summaryLength; item++) reader.SkipValue();
                    reader.ReadEndArray();
                    break;
                case 10: publicKey = reader.ReadByteString(); break;
                default: reader.SkipValue(); break;
            }
        }
        reader.ReadEndMap();
        if (publicKey.Length == 0) throw new InvalidDataException("缺少签名公钥。");
        return (publicKey, student, courseCount, scope, issuedAt);
    }

    private static byte[] ReadKid(byte[] protectedHeader)
    {
        var reader = new CborReader(protectedHeader, CborConformanceMode.Lax);
        var count = reader.ReadStartMap() ?? 0;
        byte[] kid = [];
        for (var index = 0; index < count; index++)
        {
            if (reader.ReadInt32() == 4) kid = reader.ReadByteString();
            else reader.SkipValue();
        }
        reader.ReadEndMap();
        if (kid.Length != 16) throw new InvalidDataException("密钥标识长度无效。");
        return kid;
    }

    private static CngKey OpenOrCreateKey()
    {
        foreach (var provider in new[] { CngProvider.MicrosoftPlatformCryptoProvider, CngProvider.MicrosoftSoftwareKeyStorageProvider })
        {
            try { return CngKey.Open(KeyName, provider); }
            catch (CryptographicException)
            {
                try
                {
                    return CngKey.Create(CngAlgorithm.ECDsaP256, KeyName, new CngKeyCreationParameters
                    {
                        Provider = provider,
                        KeyUsage = CngKeyUsages.Signing,
                        ExportPolicy = CngExportPolicies.None,
                        KeyCreationOptions = CngKeyCreationOptions.None
                    });
                }
                catch (CryptographicException) { }
            }
        }
        throw new CryptographicException("无法创建本机成绩单签名密钥。");
    }

    private static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) brotli.Write(value);
        return output.ToArray();
    }

    private static byte[] BuildRowsDigest(IEnumerable<(string Term, ScoreRecord Score)> rows)
    {
        var values = rows.ToList();
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartArray(values.Count);
        foreach (var item in values)
        {
            writer.WriteStartArray(7);
            writer.WriteTextString(item.Term);
            writer.WriteTextString(Normalize(item.Score.CourseCode));
            writer.WriteTextString(Normalize(item.Score.CourseName));
            writer.WriteInt32(ToFixed(item.Score.Credit, 100));
            writer.WriteTextString(Normalize(item.Score.Score));
            writer.WriteInt32(ToFixed(item.Score.Gpa, 1000));
            writer.WriteInt32(item.Score.NumericScore is double numeric ? (int)Math.Round(numeric * 10) : -1);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        return SHA256.HashData(writer.Encode());
    }

    private static byte[] DecompressBounded(byte[] value, int limit)
    {
        using var input = new MemoryStream(value);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = brotli.Read(buffer)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("解压数据超过限制。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string Fingerprint(byte[] publicKey) => Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);
    private static int ToFixed(string value, int scale) => double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var number) ? (int)Math.Round(number * scale) : -1;
    private static TranscriptVerificationResult Invalid(string message) => new(false, message, "", "", 0, "", DateTimeOffset.MinValue);
}

internal static class Base45
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 3 + 1) / 2);
        for (var i = 0; i < data.Length; i += 2)
        {
            if (i + 1 < data.Length)
            {
                var value = data[i] * 256 + data[i + 1];
                output.Append(Alphabet[value % 45]);
                output.Append(Alphabet[value / 45 % 45]);
                output.Append(Alphabet[value / 2025]);
            }
            else
            {
                output.Append(Alphabet[data[i] % 45]);
                output.Append(Alphabet[data[i] / 45]);
            }
        }
        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        var output = new List<byte>();
        for (var i = 0; i < text.Length;)
        {
            var remaining = text.Length - i;
            if (remaining == 1) throw new FormatException("Base45 长度无效。");
            var c = Value(text[i++]);
            var d = Value(text[i++]);
            if (remaining >= 3)
            {
                var e = Value(text[i++]);
                var value = c + d * 45 + e * 2025;
                if (value > ushort.MaxValue) throw new FormatException("Base45 数值无效。");
                output.Add((byte)(value / 256)); output.Add((byte)(value % 256));
            }
            else
            {
                var value = c + d * 45;
                if (value > byte.MaxValue) throw new FormatException("Base45 数值无效。");
                output.Add((byte)value);
            }
        }
        return output.ToArray();
    }

    private static int Value(char value)
    {
        var index = Alphabet.IndexOf(value);
        return index >= 0 ? index : throw new FormatException("Base45 包含非法字符。");
    }
}
