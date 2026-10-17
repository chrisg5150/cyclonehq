namespace CycloneHQ.App.Services;

// NFL team codes (MFL uses some three-letter forms like GBP/KCC) and broadcast colors for team chips.
public static class NflTeams
{
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBP"] = "GB", ["KCC"] = "KC", ["NEP"] = "NE", ["NOS"] = "NO", ["SFO"] = "SF", ["TBB"] = "TB",
        ["LVR"] = "LV", ["OAK"] = "LV", ["JAC"] = "JAX", ["WAS"] = "WSH", ["SDC"] = "LAC", ["STL"] = "LAR",
    };

    // (primary, secondary)
    static readonly Dictionary<string, (string Bg, string Fg)> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARI"] = ("#97233F", "#FFFFFF"), ["ATL"] = ("#A71930", "#FFFFFF"), ["BAL"] = ("#241773", "#FFFFFF"),
        ["BUF"] = ("#00338D", "#FFFFFF"), ["CAR"] = ("#0085CA", "#FFFFFF"), ["CHI"] = ("#0B162A", "#F26522"),
        ["CIN"] = ("#FB4F14", "#000000"), ["CLE"] = ("#311D00", "#FF3C00"), ["DAL"] = ("#041E42", "#FFFFFF"),
        ["DEN"] = ("#FB4F14", "#002244"), ["DET"] = ("#0076B6", "#FFFFFF"), ["GB"] = ("#203731", "#FFB612"),
        ["HOU"] = ("#03202F", "#E31837"), ["IND"] = ("#002C5F", "#FFFFFF"), ["JAX"] = ("#006778", "#D7A22A"),
        ["KC"] = ("#E31837", "#FFB81C"), ["LV"] = ("#000000", "#A5ACAF"), ["LAC"] = ("#0080C6", "#FFC20E"),
        ["LAR"] = ("#003594", "#FFD100"), ["MIA"] = ("#008E97", "#FFFFFF"), ["MIN"] = ("#4F2683", "#FFC62F"),
        ["NE"] = ("#002244", "#C60C30"), ["NO"] = ("#101820", "#D3BC8D"), ["NYG"] = ("#0B2265", "#A71930"),
        ["NYJ"] = ("#125740", "#FFFFFF"), ["PHI"] = ("#004C54", "#A5ACAF"), ["PIT"] = ("#101820", "#FFB612"),
        ["SF"] = ("#AA0000", "#B3995D"), ["SEA"] = ("#002244", "#69BE28"), ["TB"] = ("#D50A0A", "#FFFFFF"),
        ["TEN"] = ("#0C2340", "#4B92DB"), ["WSH"] = ("#5A1414", "#FFB612"),
    };

    public static string Code(string? team) =>
        string.IsNullOrWhiteSpace(team) ? "FA" : Aliases.TryGetValue(team, out var c) ? c : team.ToUpperInvariant();

    public static (string Bg, string Fg) Color(string? team) =>
        Colors.TryGetValue(Code(team), out var c) ? c : ("#3a4356", "#FFFFFF");
}
