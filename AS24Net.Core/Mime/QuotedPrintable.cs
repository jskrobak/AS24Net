namespace AS24Net.Core.Mime;

/// <summary>Decoding of the quoted-printable transfer encoding (RFC 2045 6.7), used by some partners for text parts.</summary>
public static class QuotedPrintable
{
    public static byte[] Decode(byte[] data)
    {
        using var result = new MemoryStream(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (b != '=')
            {
                result.WriteByte(b);
                continue;
            }

            // A soft line break: "=" at the end of a line.
            if (i + 1 < data.Length && data[i + 1] == '\n')
            {
                i += 1;
                continue;
            }

            if (i + 2 < data.Length && data[i + 1] == '\r' && data[i + 2] == '\n')
            {
                i += 2;
                continue;
            }

            if (i + 2 < data.Length && IsHex(data[i + 1]) && IsHex(data[i + 2]))
            {
                result.WriteByte((byte)(HexValue(data[i + 1]) * 16 + HexValue(data[i + 2])));
                i += 2;
                continue;
            }

            // Not a valid escape, keep it as it is.
            result.WriteByte(b);
        }

        return result.ToArray();
    }

    private static bool IsHex(byte b) => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F' or >= (byte)'a' and <= (byte)'f';

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => b - 'a' + 10,
    };
}
