using System.Numerics;
using System.Text;

namespace OnChainReader.Core;

/// <summary>Exact integer ↔ decimal-string conversion for token amounts. Floats are never used: a balance of 0.1 + 0.2 must stay 0.3.</summary>
public static class Amounts
{
    public const int EthDecimals = 18;

    /// <summary>Formats <paramref name="raw"/> / 10^<paramref name="decimals"/> without rounding and without trailing zeros ("1.5", "0", "0.000001").</summary>
    public static string Format(BigInteger raw, int decimals)
    {
        if (decimals < 0 || decimals > 77) throw new ArgumentOutOfRangeException(nameof(decimals), "decimals must be 0..77");
        var negative = raw.Sign < 0;
        var digits = BigInteger.Abs(raw).ToString();
        string result;
        if (decimals == 0)
        {
            result = digits;
        }
        else
        {
            if (digits.Length <= decimals) digits = new string('0', decimals - digits.Length + 1) + digits;
            var whole = digits[..^decimals];
            var frac = digits[^decimals..].TrimEnd('0');
            result = frac.Length == 0 ? whole : whole + "." + frac;
        }
        return negative && result != "0" ? "-" + result : result;
    }

    /// <summary>Parses a 0x-prefixed hex quantity or a 32-byte word ("0x" + 64 hex) into an unsigned integer. Empty "0x" is 0.</summary>
    public static BigInteger ParseHexUnsigned(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) throw new FormatException("empty hex value");
        var h = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (h.Length == 0) return BigInteger.Zero;
        if (!h.All(Uri.IsHexDigit)) throw new FormatException("not a hex value: " + hex);
        // a leading 0 keeps BigInteger.Parse from reading the top bit as a sign
        return BigInteger.Parse("0" + h, System.Globalization.NumberStyles.HexNumber);
    }

    /// <summary>The address stored in the low 20 bytes of a 32-byte topic ("0x000…<40 hex>") as a lower-case 0x address.</summary>
    public static string AddressFromTopic(string topic)
    {
        if (topic is null || topic.Length != 66 || !topic.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("a topic must be 0x + 64 hex characters");
        return "0x" + topic[26..].ToLowerInvariant();
    }

    /// <summary>Decodes an ABI-encoded <c>string</c> or <c>bytes32</c> return value (older tokens such as MKR return bytes32). Null if empty/unreadable.</summary>
    public static string? DecodeSymbol(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        var h = data.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? data[2..] : data;
        if (h.Length == 0 || !h.All(Uri.IsHexDigit) || h.Length % 2 != 0) return null;
        byte[] bytes;
        try { bytes = Convert.FromHexString(h); } catch (FormatException) { return null; }

        if (bytes.Length == 32) // bytes32
            return Clean(Encoding.UTF8.GetString(bytes).TrimEnd('\0'));

        if (bytes.Length >= 64) // dynamic string: offset (32) + length (32) + data
        {
            var offset = (int)(ParseHexUnsigned(Convert.ToHexString(bytes[..32])) & int.MaxValue);
            if (offset + 32 > bytes.Length) return null;
            var len = (int)(ParseHexUnsigned(Convert.ToHexString(bytes[offset..(offset + 32)])) & int.MaxValue);
            if (len <= 0 || offset + 32 + len > bytes.Length) return null;
            return Clean(Encoding.UTF8.GetString(bytes, offset + 32, len));
        }
        return null;

        static string? Clean(string s)
        {
            s = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return s.Length is > 0 and <= 32 ? s : null;
        }
    }
}
