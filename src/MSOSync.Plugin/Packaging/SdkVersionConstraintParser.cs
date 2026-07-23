namespace MSOSync.Plugin.Packaging;

/// <summary>
/// Parses a minimal npm-style semver range into a Version predicate.
/// Supported forms: &gt;=X.Y.Z  &gt;X.Y.Z  &lt;=X.Y.Z  &lt;X.Y.Z  =X.Y.Z  X.Y.Z
/// (space-AND of two comparators is also supported, e.g. "&gt;=1.0.0 &lt;2.0.0").
/// </summary>
public static class SdkVersionConstraintParser
{
    /// <summary>
    /// Parse <paramref name="constraint"/> into a predicate.
    /// Returns null if the constraint string is invalid or unparseable.
    /// </summary>
    public static Func<Version, bool>? Parse(string constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return null;

        var parts = constraint.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
            return ParseComparator(parts[0]);

        if (parts.Length == 2)
        {
            var left  = ParseComparator(parts[0]);
            var right = ParseComparator(parts[1]);
            if (left is null || right is null) return null;
            return v => left(v) && right(v);
        }

        return null;
    }

    /// <summary>
    /// Returns true if <paramref name="hostVersion"/> satisfies <paramref name="constraint"/>.
    /// Returns false if the constraint is unparseable (treated as incompatible).
    /// </summary>
    public static bool Satisfies(string constraint, Version hostVersion)
        => Parse(constraint)?.Invoke(hostVersion) ?? false;

    private static Func<Version, bool>? ParseComparator(string part)
    {
        string op;
        string vstr;

        if (part.StartsWith(">=", StringComparison.Ordinal))
        {
            op   = ">=";
            vstr = part[2..];
        }
        else if (part.StartsWith("<=", StringComparison.Ordinal))
        {
            op   = "<=";
            vstr = part[2..];
        }
        else if (part.StartsWith(">", StringComparison.Ordinal))
        {
            op   = ">";
            vstr = part[1..];
        }
        else if (part.StartsWith("<", StringComparison.Ordinal))
        {
            op   = "<";
            vstr = part[1..];
        }
        else if (part.StartsWith("=", StringComparison.Ordinal))
        {
            op   = "=";
            vstr = part[1..];
        }
        else
        {
            // bare version treated as exact match
            op   = "=";
            vstr = part;
        }

        if (!Version.TryParse(EnsureThreeParts(vstr), out var v)) return null;

        return op switch
        {
            ">=" => host => host >= v,
            ">"  => host => host >  v,
            "<=" => host => host <= v,
            "<"  => host => host <  v,
            "="  => host => host == v,
            _    => null,
        };
    }

    // Ensure at least major.minor.patch so Version.TryParse works consistently.
    private static string EnsureThreeParts(string v)
    {
        var parts = v.Split('.');
        return parts.Length switch
        {
            1 => $"{v}.0.0",
            2 => $"{v}.0",
            _ => v,
        };
    }
}
