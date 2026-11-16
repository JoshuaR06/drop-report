namespace BootyBoard.Analysis;

/// <summary>A log-entry headline and standfirst for a match, written from its stats (the editorial look).</summary>
public static class Headlines
{
    private static readonly string[] Words =
        { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen",
          "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty" };

    public static string Word(int n) => n >= 0 && n < Words.Length ? Words[n] : n.ToString("N0");

    public static string Ordinal(int n)
    {
        var s = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n + s;
    }

    public static (string Headline, string Dek) For(MatchSummary m)
    {
        var at = string.IsNullOrEmpty(m.Haunt) ? "" : $" at {m.Haunt}";
        var elims = m.Epic.Elims ?? m.Fights.Elims;
        var chests = m.Chests.Opened + m.Chests.Broken + m.Chests.Team.Sum(t => t.Opened + t.Broken);
        string head =
            m.Placement == 1 ? $"Victory Royale{at}" :
            elims >= 8 ? $"{Word(elims)} down{at}" :
            chests >= 30 ? $"{chests} chests opened{at}" :
            m.Placement is int p && p <= 5 ? $"{Ordinal(p)} place{at}" :
            m.Death?.By == "player" && m.Death.Dist is double d && d >= 60 ? $"Picked off from {d:0} m{at}" :
            m.Placement is int q ? $"{Ordinal(q)} place after {m.Minutes:0} minutes{at}" : $"A {m.Minutes:0}-minute match{at}";

        var parts = new List<string>();
        parts.Add(elims == 1 ? "one elimination" : $"{Word(elims).ToLowerInvariant()} eliminations");
        if (m.Fights.Knocks > 0) parts.Add(m.Fights.Knocks == 1 ? "one knock" : $"{Word(m.Fights.Knocks).ToLowerInvariant()} knocks");
        if (chests > 0) parts.Add($"{chests} chests for the team");
        var dek = string.Join(", ", parts);
        dek = char.ToUpperInvariant(dek[0]) + dek[1..];
        if (m.Death != null)
            dek += m.Death.Cause == "bled out" || m.Death.By == "me" ? ". Bled out in the end." :
                   $". Went down to a {m.Death.Cause}{(m.Death.Dist is double dd ? $" from {dd:0} m" : "")}.";
        else if (m.Placement == 1) dek += ". Last team standing.";
        else dek += ".";
        return (head, dek);
    }
}
