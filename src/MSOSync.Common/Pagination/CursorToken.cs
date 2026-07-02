using System.Text;

namespace MSOSync.Common.Pagination;

public static class CursorToken
{
    public static string Encode(long id, long ticks)
    {
        var raw = $"v1:{id}:{ticks}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public static (long Id, long Ticks) Decode(string token)
    {
        string raw;
        try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(token)); }
        catch { throw new ArgumentException("Invalid cursor token."); }

        var parts = raw.Split(':');
        if (parts.Length != 3 || parts[0] != "v1")
            throw new ArgumentException("Invalid cursor token format.");

        if (!long.TryParse(parts[1], out var id) || !long.TryParse(parts[2], out var ticks))
            throw new ArgumentException("Invalid cursor token values.");

        return (id, ticks);
    }
}
