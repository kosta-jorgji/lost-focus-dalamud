using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LostFocus.Api;
using Lumina.Excel.Sheets;

namespace LostFocus.Tracking;

/// <summary>Reads the current game state into a SnapshotDto. Must be called on the framework thread.</summary>
public sealed class GameStateReader
{
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly IDataManager data;
    private readonly Configuration config;

    public GameStateReader(IClientState clientState, IObjectTable objects, ICondition condition, IDataManager data, Configuration config)
    {
        this.clientState = clientState;
        this.objects = objects;
        this.condition = condition;
        this.data = data;
        this.config = config;
    }

    public PrivacyDto Privacy() => new()
    {
        Playtime = config.SharePlaytime,
        Zone = config.ShareZone,
        Deaths = config.ShareDeaths,
        Gear = config.ShareGear,
        Pvp = config.SharePvp,
        Emotes = config.ShareEmotes,
        Location = config.ShareLocation,
    };

    public SnapshotDto Read(int pullNumber)
    {
        var snap = new SnapshotDto { Privacy = Privacy() };
        var player = objects.LocalPlayer;
        snap.Online = clientState.IsLoggedIn && player != null;
        if (player == null) return snap;

        snap.CharacterName = player.Name.TextValue;

        // Anything turned off is simply never read, so it can't leak.
        if (config.ShareGear)
        {
            snap.Job = player.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? "";
            snap.Level = player.Level;
            snap.ItemLevel = ReadItemLevel();
        }

        if (config.ShareZone)
        {
            snap.Zone = ZoneName(clientState.TerritoryType);
            snap.Duty = DutyName(clientState.TerritoryType);
            snap.InCombat = condition[ConditionFlag.InCombat];
            snap.PullNumber = pullNumber;
        }

        if (config.SharePvp)
            snap.Pvp = ReadPvpProfile();

        return snap;
    }

    public string ZoneName(uint territoryId)
    {
        var row = data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
        return row?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
    }

    public string? DutyName(uint territoryId)
    {
        var row = data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
        var cfc = row?.ContentFinderCondition.ValueNullable;
        if (cfc == null || cfc.Value.RowId == 0) return null;
        var name = cfc.Value.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public string? EmoteName(uint emoteId)
    {
        var name = data.GetExcelSheet<Emote>().GetRowOrDefault(emoteId)?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public bool IsPvpTerritory(uint territoryId)
    {
        var row = data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
        return row?.IsPvpZone ?? false;
    }

    /// Average item level of equipped gear, the way the character window computes it:
    /// 12 slots, belt excluded, main hand counted twice when there's no off-hand.
    private unsafe int ReadItemLevel()
    {
        var inv = InventoryManager.Instance();
        if (inv == null) return 0;
        var container = inv->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null || !container->IsLoaded) return 0;

        var items = data.GetExcelSheet<Item>();
        var total = 0;
        var mainHand = 0;
        var hasOffHand = false;

        for (var slot = 0; slot < 13; slot++)
        {
            if (slot == 5) continue; // belt
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0) continue;
            var row = items.GetRowOrDefault(item->ItemId);
            if (row == null) continue;
            var ilvl = (int)row.Value.LevelItem.RowId;
            if (slot == 0) mainHand = ilvl;
            if (slot == 1) hasOffHand = true;
            total += ilvl;
        }

        if (!hasOffHand) total += mainHand;
        return total / 12;
    }

    private unsafe PvpProfileDto? ReadPvpProfile()
    {
        var ui = UIState.Instance();
        if (ui == null) return null;
        ref var p = ref ui->PvPProfile;
        if (!p.IsLoaded) return null;

        return new PvpProfileDto
        {
            FrontlineTotalMatches = (int)p.FrontlineTotalMatches,
            FrontlineTotalFirst = (int)p.FrontlineTotalFirstPlace,
            FrontlineTotalSecond = (int)p.FrontlineTotalSecondPlace,
            FrontlineTotalThird = (int)p.FrontlineTotalThirdPlace,
            FrontlineWeeklyMatches = p.FrontlineWeeklyMatches,
            FrontlineWeeklyFirst = p.FrontlineWeeklyFirstPlace,
            FrontlineWeeklySecond = p.FrontlineWeeklySecondPlace,
            FrontlineWeeklyThird = p.FrontlineWeeklyThirdPlace,
            CcCasualMatches = (int)p.CrystallineConflictCasualMatches,
            CcCasualWins = (int)p.CrystallineConflictCasualMatchesWon,
            CcRankedMatches = (int)p.CrystallineConflictRankedMatches,
            CcRankedWins = (int)p.CrystallineConflictRankedMatchesWon,
            CcRank = p.CrystallineConflictCurrentRank,
            CcRisingStars = p.CrystallineConflictCurrentRisingStars,
            RivalWingsTotalMatches = (int)p.RivalWingsTotalMatches,
            RivalWingsTotalWins = (int)p.RivalWingsTotalMatchesWon,
            SeriesRank = p.SeriesCurrentRank,
        };
    }
}
