using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LostFocus.Api;
using Lumina.Excel.Sheets;
using Map = Lumina.Excel.Sheets.Map;

namespace LostFocus.Tracking;

/// <summary>
/// Live map position, on its own interval (config.LocationSeconds). Reading it is just a
/// position + a couple of sheet lookups, and it's only sent when the coordinates shown on the
/// in-game map change, so standing still (or AFK) costs nothing. In housing districts it also
/// sends the ward / plot / apartment.
/// </summary>
public sealed unsafe class LocationTracker
{
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly ApiClient api;

    private DateTime lastRead = DateTime.MinValue;
    private LocationDto? lastSent;

    public LocationTracker(IClientState clientState, IObjectTable objects, IDataManager data, Configuration config, ApiClient api)
    {
        this.clientState = clientState;
        this.objects = objects;
        this.data = data;
        this.config = config;
        this.api = api;
    }

    /// Called every framework tick.
    public void Tick()
    {
        if (!config.ShareLocation) return;
        var now = DateTime.UtcNow;
        if (now - lastRead < TimeSpan.FromSeconds(Math.Clamp(config.LocationSeconds, 1, 60))) return;
        lastRead = now;

        if (!clientState.IsLoggedIn) return;
        var player = objects.LocalPlayer;
        if (player == null) return; // loading screen

        var loc = Read(player.Position);
        if (lastSent != null && Same(lastSent, loc)) return;
        api.SendLocation(loc);
        lastSent = loc;
    }

    /// Forget what was sent so the next tick sends again (after logout, or when switched back on).
    public void Reset()
    {
        lastSent = null;
        lastRead = DateTime.MinValue;
    }

    private LocationDto Read(Vector3 pos)
    {
        var territory = data.GetExcelSheet<TerritoryType>().GetRowOrDefault(clientState.TerritoryType);
        var loc = new LocationDto
        {
            Zone = territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
            Housing = Housing(),
        };

        var info = TerritoryInfo.Instance();
        if (info != null && info->SubAreaPlaceNameId != 0)
        {
            var sub = data.GetExcelSheet<PlaceName>().GetRowOrDefault(info->SubAreaPlaceNameId)?.Name.ExtractText();
            if (!string.IsNullOrEmpty(sub)) loc.SubArea = sub;
        }

        var map = data.GetExcelSheet<Map>().GetRowOrDefault(clientState.MapId);
        if (map == null || map.Value.RowId == 0 || map.Value.SizeFactor == 0) return loc;

        // Same maths as the game's map: world position -> 0..1 across the 2048px texture -> "X 11.0".
        var scale = map.Value.SizeFactor / 100.0;
        var fx = ((pos.X + map.Value.OffsetX) * scale + 1024.0) / 2048.0;
        var fy = ((pos.Z + map.Value.OffsetY) * scale + 1024.0) / 2048.0;
        loc.MapPath = map.Value.Id.ExtractText();
        loc.Fx = Math.Round(fx, 4);
        loc.Fy = Math.Round(fy, 4);
        loc.X = Math.Round(41.0 / scale * fx + 1.0, 1);
        loc.Y = Math.Round(41.0 / scale * fy + 1.0, 1);
        return loc;
    }

    /// "Ward 12 · Plot 34 · inside", "Ward 5 (subdivision) · Apartment 17", or null outside housing.
    private static string? Housing()
    {
        var housing = HousingManager.Instance();
        if (housing == null || (housing->OutdoorTerritory == null && housing->IndoorTerritory == null)) return null;

        var ward = housing->GetCurrentWard(); // 0-based, negative when unknown
        if (ward < 0) return null;
        var parts = new List<string>
        {
            housing->GetCurrentDivision() == 2 ? $"Ward {ward + 1} (subdivision)" : $"Ward {ward + 1}",
        };

        var plot = housing->GetCurrentPlot(); // 0-based; negative for apartment buildings
        var room = housing->GetCurrentRoom(); // apartment number, or FC private chamber
        if (plot >= 0) parts.Add($"Plot {plot + 1}");
        if (room > 0) parts.Add(plot >= 0 ? $"Room {room}" : $"Apartment {room}");
        if (housing->IsInside()) parts.Add("inside");
        return string.Join(" · ", parts);
    }

    /// Same place as far as the site can tell: same map and the same one-decimal coordinates.
    private static bool Same(LocationDto a, LocationDto b) =>
        a.Zone == b.Zone && a.SubArea == b.SubArea && a.Housing == b.Housing && a.MapPath == b.MapPath && a.X == b.X && a.Y == b.Y;
}
