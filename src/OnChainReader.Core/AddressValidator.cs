using Nethereum.Util;

namespace OnChainReader.Core;

public static class AddressValidator
{
    /// <summary>
    /// Accepts "0x" + 40 hex characters. A mixed-case address must carry a valid EIP-55 checksum (a typo in a checksummed address is caught here
    /// instead of silently querying a different account); all-lower or all-upper addresses are accepted as-is. Returns the lower-case form.
    /// </summary>
    public static string Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidRequestException("address is required");
        var a = address.Trim();
        if (a.Length != 42 || !a.StartsWith("0x", StringComparison.Ordinal) || !a[2..].All(Uri.IsHexDigit))
            throw new InvalidRequestException("address must be 0x followed by 40 hexadecimal characters");
        var body = a[2..];
        var mixed = body.Any(char.IsLower) && body.Any(char.IsUpper);
        if (mixed && !AddressUtil.Current.IsChecksumAddress(a))
            throw new InvalidRequestException("address has an invalid EIP-55 checksum (check for a typo)");
        return a.ToLowerInvariant();
    }
}
