namespace MetaHub.Identification.Hashing;

/// <summary>
/// CRC32 (IEEE 802.3, reflected) — the checksum commonly embedded in anime release
/// filenames and exposed by AniDB. Optional secondary signal alongside ED2K.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static async Task<string> ComputeFileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);

        uint crc = 0xFFFFFFFF;
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await fs.ReadAsync(buffer, ct)) > 0)
        {
            for (int i = 0; i < read; i++)
                crc = (crc >> 8) ^ Table[(crc ^ buffer[i]) & 0xFF];
        }

        return (~crc).ToString("x8");
    }

    private static uint[] BuildTable()
    {
        const uint poly = 0xEDB88320;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? poly ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
