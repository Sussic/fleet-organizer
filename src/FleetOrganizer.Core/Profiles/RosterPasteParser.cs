namespace FleetOrganizer.Core.Profiles;

public sealed record ParsedRosterEntry(
    string CharacterName,
    string? SquadName,
    string? RoleText);

public static class RosterPasteParser
{
    private static readonly string[] StructuredSeparators = [" — ", " – ", " | "];
    private static readonly char[] SimpleSeparators = [',', '\t'];

    public static ParsedRosterEntry[] Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var entries = new List<ParsedRosterEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = input
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var tabFields = line.Split(
                '\t',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tabFields.Length >= 3)
            {
                Add(entries, names, tabFields[0], tabFields[1], tabFields[2]);
                continue;
            }

            var structuredFields = SplitStructured(line);
            if (structuredFields.Length >= 2)
            {
                Add(
                    entries,
                    names,
                    structuredFields[0],
                    structuredFields[1],
                    structuredFields.Length >= 3 ? structuredFields[2] : null);
                continue;
            }

            var simpleNames = line.Split(
                SimpleSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var simpleName in simpleNames)
            {
                Add(entries, names, simpleName, null, null);
            }
        }

        return entries.ToArray();
    }

    private static string[] SplitStructured(string line)
    {
        foreach (var separator in StructuredSeparators)
        {
            if (line.Contains(separator, StringComparison.Ordinal))
            {
                return line.Split(
                    separator,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return [];
    }

    private static void Add(
        List<ParsedRosterEntry> entries,
        HashSet<string> names,
        string characterName,
        string? squadName,
        string? roleText)
    {
        var trimmedName = characterName.Trim();
        if (trimmedName.Length == 0 || !names.Add(trimmedName))
        {
            return;
        }

        entries.Add(new ParsedRosterEntry(
            trimmedName,
            NullIfWhiteSpace(squadName),
            NullIfWhiteSpace(roleText)));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
