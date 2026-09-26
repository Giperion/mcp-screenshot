namespace ScreenshotMcp.Imaging;

/// <summary>CRC-32 (ISO-HDLC, reflected polynomial 0xEDB88320) as used by PNG chunks.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var table = Table;
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = table[(byte)(crc ^ value)] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
