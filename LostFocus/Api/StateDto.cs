using System.Collections.Generic;
using System.Text.Json.Serialization;

// Keep in sync with backend/src/types.ts and frontend/src/lib/live.ts in the site repo (kosta-jorgji/lost-focus)
namespace LostFocus.Api;

public sealed class PrivacyDto
{
    [JsonPropertyName("playtime")] public bool Playtime { get; set; }
    [JsonPropertyName("zone")] public bool Zone { get; set; }
    [JsonPropertyName("deaths")] public bool Deaths { get; set; }
    [JsonPropertyName("gear")] public bool Gear { get; set; }
    [JsonPropertyName("pvp")] public bool Pvp { get; set; }
    [JsonPropertyName("emotes")] public bool Emotes { get; set; }
    [JsonPropertyName("location")] public bool Location { get; set; }
}

/// Where he is on the zone map. Sent on its own interval, only when the shown coordinates change.
public sealed class LocationDto
{
    [JsonPropertyName("zone")] public string Zone { get; set; } = "";
    [JsonPropertyName("subArea")] public string? SubArea { get; set; }
    /// In a housing district: "Ward 12 · Plot 34 · inside", "Ward 5 (subdivision) · Apartment 17".
    [JsonPropertyName("housing")] public string? Housing { get; set; }
    /// Map texture path, e.g. "s1t2/01"; the site loads the map image from it.
    [JsonPropertyName("mapPath")] public string? MapPath { get; set; }
    /// In-game map coordinates, as shown on the map (X 11.0, Y 11.4).
    [JsonPropertyName("x")] public double? X { get; set; }
    [JsonPropertyName("y")] public double? Y { get; set; }
    /// Position across the 2048px map image, 0..1.
    [JsonPropertyName("fx")] public double? Fx { get; set; }
    [JsonPropertyName("fy")] public double? Fy { get; set; }
}

public sealed class PvpProfileDto
{
    [JsonPropertyName("frontlineTotalMatches")] public int FrontlineTotalMatches { get; set; }
    [JsonPropertyName("frontlineTotalFirst")] public int FrontlineTotalFirst { get; set; }
    [JsonPropertyName("frontlineTotalSecond")] public int FrontlineTotalSecond { get; set; }
    [JsonPropertyName("frontlineTotalThird")] public int FrontlineTotalThird { get; set; }
    [JsonPropertyName("frontlineWeeklyMatches")] public int FrontlineWeeklyMatches { get; set; }
    [JsonPropertyName("frontlineWeeklyFirst")] public int FrontlineWeeklyFirst { get; set; }
    [JsonPropertyName("frontlineWeeklySecond")] public int FrontlineWeeklySecond { get; set; }
    [JsonPropertyName("frontlineWeeklyThird")] public int FrontlineWeeklyThird { get; set; }
    [JsonPropertyName("ccCasualMatches")] public int CcCasualMatches { get; set; }
    [JsonPropertyName("ccCasualWins")] public int CcCasualWins { get; set; }
    [JsonPropertyName("ccRankedMatches")] public int CcRankedMatches { get; set; }
    [JsonPropertyName("ccRankedWins")] public int CcRankedWins { get; set; }
    [JsonPropertyName("ccRank")] public int CcRank { get; set; }
    [JsonPropertyName("ccRisingStars")] public int CcRisingStars { get; set; }
    [JsonPropertyName("rivalWingsTotalMatches")] public int RivalWingsTotalMatches { get; set; }
    [JsonPropertyName("rivalWingsTotalWins")] public int RivalWingsTotalWins { get; set; }
    [JsonPropertyName("seriesRank")] public int SeriesRank { get; set; }
}

public sealed class SnapshotDto
{
    [JsonPropertyName("online")] public bool Online { get; set; }
    [JsonPropertyName("characterName")] public string CharacterName { get; set; } = "";
    [JsonPropertyName("job")] public string Job { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("itemLevel")] public int ItemLevel { get; set; }
    [JsonPropertyName("zone")] public string Zone { get; set; } = "";
    [JsonPropertyName("duty")] public string? Duty { get; set; }
    [JsonPropertyName("inCombat")] public bool InCombat { get; set; }
    [JsonPropertyName("pullNumber")] public int PullNumber { get; set; }
    [JsonPropertyName("privacy")] public PrivacyDto Privacy { get; set; } = new();
    [JsonPropertyName("pvp")] public PvpProfileDto? Pvp { get; set; }
}

public sealed class EventDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("data")] public Dictionary<string, object?>? Data { get; set; }
}
