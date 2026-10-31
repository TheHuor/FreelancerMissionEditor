namespace MissionEditor.Core.Hashing;

/// <summary>
/// Freelancer's CreateID: the 30-bit nickname hash used for triggers, bases, systems and
/// most other ids. It is case-insensitive, so two nicknames that differ only in case are the
/// same id for the engine.
/// </summary>
public static class FlHash
{
    private const uint Polynomial = 0xA001u << 14;

    private static readonly uint[] Table = BuildTable();

    /// <summary>Computes CreateID for a nickname (e.g. "li01_01_base" = 2804956685).</summary>
    public static uint CreateId(string nickname)
    {
        uint hash = 0;
        foreach (char character in nickname)
        {
            uint lower = char.ToLowerInvariant(character) & 0xFFu;
            hash = (hash >> 8) ^ Table[(hash ^ lower) & 0xFF];
        }
        hash = (hash >> 24) | ((hash >> 8) & 0x0000FF00u) | ((hash << 8) & 0x00FF0000u) | (hash << 24);
        return (hash >> 2) | 0x80000000u;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            table[i] = crc;
        }
        return table;
    }
}
