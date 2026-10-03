using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Competitions.Directions;

public sealed class CompetitionDirection
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    [MaxLength(96)] public string Name { get; set; } = string.Empty;
    [MaxLength(96)] public string NormalizedName { get; set; } = string.Empty;
    [MaxLength(80)] public string Icon { get; set; } = "flag";
    [MaxLength(96)] public string? TemplateDirection { get; set; }
    public int Position { get; set; }
}

public static class CompetitionDirectionDefaults
{
    private static readonly (string Name, string Icon)[] Presets =
    [
        ("Misc", "puzzle"), ("Web", "globe"), ("Crypto", "key-round"),
        ("Pwn", "bug"), ("Reverse", "binary"), ("Penetration", "scan-search"),
        ("Forensics", "scan-search"), ("OSINT", "search"), ("AI", "bot"),
        ("Mobile", "smartphone"), ("IoT", "cpu"), ("Hardware", "circuit-board"),
        ("Cloud", "cloud"), ("Blockchain", "link-2")
    ];

    public static List<CompetitionDirection> Create(Guid competitionId) =>
        Presets.Select((preset, position) => new CompetitionDirection
        {
            Id = Guid.NewGuid(), CompetitionId = competitionId, Name = preset.Name,
            NormalizedName = preset.Name.ToUpperInvariant(), TemplateDirection = preset.Name.ToUpperInvariant(), Icon = preset.Icon, Position = position
        }).ToList();

    public static string CanonicalName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "" => "MISC",
        "REV" => "REVERSE", "PENTEST" => "PENETRATION",
        "FORENSIC" or "DFIR" => "FORENSICS",
        "RECON" or "OPEN SOURCE INTELLIGENCE" => "OSINT",
        "ML" or "LLM" or "MACHINE LEARNING" => "AI", "EMBEDDED" => "HARDWARE",
        "WEB3" or "ETH" => "BLOCKCHAIN", var value => value
    };
}
