using System;
using System.Collections.Generic;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using LostFocus.Api;

namespace LostFocus.Tracking;

/// <summary>
/// Watches for the discrete things the site cares about (deaths, wipes, pulls,
/// login/logout, Frontline results) and hands them to the API client.
/// </summary>
public sealed class EventTracker : IDisposable
{
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly IDutyState dutyState;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly GameStateReader reader;
    private readonly ApiClient api;

    public int PullNumber { get; private set; }

    private DateTime? deadSince;
    private PvpProfileDto? pvpAtEntry;
    private bool inPvpTerritory;

    public EventTracker(
        IClientState clientState, IObjectTable objects, IDutyState dutyState, IPluginLog log,
        Configuration config, GameStateReader reader, ApiClient api)
    {
        this.clientState = clientState;
        this.objects = objects;
        this.dutyState = dutyState;
        this.log = log;
        this.config = config;
        this.reader = reader;
        this.api = api;

        clientState.Login += OnLogin;
        clientState.Logout += OnLogout;
        clientState.TerritoryChanged += OnTerritoryChanged;
        dutyState.DutyStarted += OnDutyStarted;
        dutyState.DutyRecommenced += OnDutyStarted;
        dutyState.DutyWiped += OnDutyWiped;
        dutyState.DutyCompleted += OnDutyCompleted;
    }

    /// Called every framework tick. Death and revive are the IsDead edges on the local player.
    /// The player is briefly null during loading screens; that doesn't end a nap.
    public void Tick()
    {
        var player = objects.LocalPlayer;
        if (player == null) return;

        var dead = player.IsDead;
        if (dead && deadSince == null)
        {
            deadSince = DateTime.UtcNow;
            OnDeath();
        }
        else if (!dead && deadSince != null)
        {
            OnRevive();
        }
    }

    /// Floor time: how long he stayed dead before a raise, a release or the wipe reset.
    private void OnRevive()
    {
        var seconds = (int)(DateTime.UtcNow - deadSince!.Value).TotalSeconds;
        deadSince = null;
        if (config.ShareDeaths) Emit("revive", new() { ["seconds"] = seconds });
    }

    private void OnDeath()
    {
        if (!config.ShareDeaths) return;
        Emit("death", new() { ["zone"] = config.ShareZone ? reader.ZoneName(clientState.TerritoryType) : null });
    }

    private void OnLogin()
    {
        if (config.SharePlaytime) Emit("login");
    }

    private void OnLogout(int type, int code)
    {
        PullNumber = 0;
        // Logging out while dead ends the nap, otherwise the next login would count the offline hours.
        if (deadSince != null) OnRevive();
        if (config.SharePlaytime) Emit("logout");
    }

    private void OnTerritoryChanged(uint territory)
    {
        PullNumber = 0;

        // Frontline / CC results: diff the PvP profile between entering and leaving a PvP zone.
        var nowPvp = reader.IsPvpTerritory(territory);
        if (nowPvp && !inPvpTerritory)
        {
            pvpAtEntry = config.SharePvp ? reader.Read(0).Pvp : null;
        }
        else if (!nowPvp && inPvpTerritory && pvpAtEntry != null && config.SharePvp)
        {
            var after = reader.Read(0).Pvp;
            if (after != null) EmitPvpResult(pvpAtEntry, after);
            pvpAtEntry = null;
        }
        inPvpTerritory = nowPvp;
    }

    private void EmitPvpResult(PvpProfileDto before, PvpProfileDto after)
    {
        if (after.FrontlineTotalMatches > before.FrontlineTotalMatches)
        {
            var place = after.FrontlineTotalFirst > before.FrontlineTotalFirst ? 1
                : after.FrontlineTotalSecond > before.FrontlineTotalSecond ? 2
                : after.FrontlineTotalThird > before.FrontlineTotalThird ? 3 : 0;
            Emit("pvp_match", new() { ["mode"] = "frontline", ["place"] = place });
        }
        else if (after.CcCasualMatches + after.CcRankedMatches > before.CcCasualMatches + before.CcRankedMatches)
        {
            var won = after.CcCasualWins + after.CcRankedWins > before.CcCasualWins + before.CcRankedWins;
            Emit("pvp_match", new() { ["mode"] = "cc", ["won"] = won });
        }
        else if (after.RivalWingsTotalMatches > before.RivalWingsTotalMatches)
        {
            Emit("pvp_match", new() { ["mode"] = "rivalwings", ["won"] = after.RivalWingsTotalWins > before.RivalWingsTotalWins });
        }
    }

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        PullNumber++;
        if (config.ShareZone) Emit("pull", new() { ["duty"] = reader.DutyName(args.TerritoryType.RowId), ["pull"] = PullNumber });
    }

    private void OnDutyWiped(IDutyStateEventArgs args)
    {
        if (config.ShareDeaths) Emit("wipe", new() { ["duty"] = config.ShareZone ? reader.DutyName(args.TerritoryType.RowId) : null, ["pull"] = PullNumber });
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        if (config.ShareZone) Emit("duty_complete", new() { ["duty"] = reader.DutyName(args.TerritoryType.RowId), ["pulls"] = PullNumber });
    }

    private void Emit(string type, Dictionary<string, object?>? data = null)
    {
        log.Debug("[event] {Type}", type);
        api.SendEvent(new EventDto { Type = type, At = DateTime.UtcNow.ToString("o"), Data = data });
    }

    public void Dispose()
    {
        clientState.Login -= OnLogin;
        clientState.Logout -= OnLogout;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        dutyState.DutyStarted -= OnDutyStarted;
        dutyState.DutyRecommenced -= OnDutyStarted;
        dutyState.DutyWiped -= OnDutyWiped;
        dutyState.DutyCompleted -= OnDutyCompleted;
    }
}
