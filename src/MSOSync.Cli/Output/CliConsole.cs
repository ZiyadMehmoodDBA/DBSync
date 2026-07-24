namespace MSOSync.Cli.Output;

public static class CliConsole
{
    public static void Ok(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK]  {message}");
        Console.ResetColor();
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WRN] {message}");
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERR] {message}");
        Console.ResetColor();
    }

    public static void Info(string message)
    {
        Console.ResetColor();
        Console.WriteLine(message);
    }

    /// <summary>
    /// Renders a left-aligned table. Columns padded to max column width + 2 spaces.
    /// Header row followed by a separator of dashes.
    /// </summary>
    public static void Table(string[] headers, IEnumerable<string[]> rows)
    {
        var allRows = rows.ToList();
        int colCount = headers.Length;
        int[] widths = new int[colCount];

        for (int i = 0; i < colCount; i++)
            widths[i] = headers[i].Length;

        foreach (string[] row in allRows)
            for (int i = 0; i < colCount && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        // Header
        Console.WriteLine(FormatRow(headers, widths));
        // Separator
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        // Data rows
        foreach (string[] row in allRows)
            Console.WriteLine(FormatRow(row, widths));
    }

    private static string FormatRow(string[] cells, int[] widths)
        => string.Join("  ", cells.Select((c, i) => i < widths.Length ? c.PadRight(widths[i]) : c));
}
