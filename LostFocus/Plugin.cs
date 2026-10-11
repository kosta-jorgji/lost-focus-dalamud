using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LostFocus.Api;
using LostFocus.Tracking;
using LostFocus.Windows;

namespace LostFocus;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IObjectTable Objects { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IDutyState DutyState { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IGameInteropProvider Interop { get; set; } = null!;
    [PluginService] private static IChatGui Chat { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private const string Command = "/lostfocus";

    /// How often the game state is read and sent (and emote counts flushed). Fixed and slow on
    /// purpose: reading gear / PvP / sheets is the only non-trivial work this plugin does per tick.
    public const int HeartbeatSeconds = 30;

    private readonly Configuration config;
    private readonly ApiClient api;
    private readonly GameStateReader reader;
    private readonly EventTracker events;
    private readonly EmoteTracker emotes;
    private readonly LocationTracker location;
    private readonly PlaytimeTracker playtime;
    private readonly ActivityTracker activity;
    private readonly WindowSystem windows = new("LostFocus");
    private readonly ConfigWindow configWindow;

    private DateTime lastHeartbeat = DateTime.MinValue;
    private SnapshotDto? lastSnapshot;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        api = new ApiClient(config, Log);
        reader = new GameStateReader(ClientState, Objects, Condition, DataManager, config);
        events = new EventTracker(ClientState, Objects, DutyState, Log, config, reader, api);
        emotes = new EmoteTracker(Interop, Log, config, reader, api);
        location = new LocationTracker(ClientState, Objects, DataManager, config, api);
        playtime = new PlaytimeTracker(ClientState, Objects, Condition, Chat, Log, config, api);
        activity = new ActivityTracker(config, ClientState, Objects, Condition);

        configWindow = new ConfigWindow(config, api, SendNow, GoOffline);
        windows.AddWindow(configWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfig;

        Commands.AddHandler(Command, new CommandInfo((_, _) => ToggleConfig())
        {
            HelpMessage = "Open the Lost Focus tracker settings.",
        });

        Framework.Update += OnUpdate;
        ClientState.Logout += OnLogout;
    }

    private void ToggleConfig() => configWindow.Toggle();

    private void OnUpdate(IFramework _)
    {
        if (!api.IsConfigured) { activity.Stop(); return; }
        events.Tick();
        emotes.Tick();
        location.Tick();
        playtime.Tick();
        var activityChanged = activity.Tick();
        SendHeartbeat(force: activityChanged);
    }

    /// "Send now" / a privacy toggle changed: full snapshot, and the location again if it's on.
    private void SendNow()
    {
        location.Reset();
        activity.Tick();
        SendHeartbeat(force: true);
    }

    private void SendHeartbeat(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now - lastHeartbeat < TimeSpan.FromSeconds(HeartbeatSeconds)) return;

        // Only while he's actually in the game. Title screen / character select: logout already told
        // the site he's offline. Loading screen: there's no player object, so every field would read
        // as empty and the site would flash offline; keep the last snapshot until he's back.
        if (!ClientState.IsLoggedIn) return;
        if (Objects.LocalPlayer == null)
        {
            // Keep the normal game state through loading, but tell activity timers to pause.
            if (force && lastSnapshot != null)
            {
                var loading = new SnapshotDto
                {
                    Online = true, CharacterName = lastSnapshot.CharacterName, Privacy = reader.Privacy(),
                    Job = config.ShareGear ? lastSnapshot.Job : "",
                    Level = config.ShareGear ? lastSnapshot.Level : 0,
                    ItemLevel = config.ShareGear ? lastSnapshot.ItemLevel : 0,
                    Zone = config.ShareZone ? lastSnapshot.Zone : "",
                    Duty = config.ShareZone ? lastSnapshot.Duty : null,
                    InCombat = config.ShareZone && lastSnapshot.InCombat,
                    PullNumber = config.ShareZone ? lastSnapshot.PullNumber : 0,
                    Pvp = config.SharePvp ? lastSnapshot.Pvp : null,
                };
                activity.Fill(loading);
                api.SendHeartbeat(loading);
                lastHeartbeat = now;
            }
            return;
        }

        var snapshot = reader.Read(events.PullNumber);
        activity.Fill(snapshot);
        api.SendHeartbeat(snapshot);
        lastSnapshot = snapshot;
        lastHeartbeat = now;
    }

    private void OnLogout(int type, int code) => GoOffline();

    /// Explicit offline snapshot so the site flips to offline immediately instead of waiting out the
    /// backend's stale timeout (~2.5 min). Used on logout, when tracking is switched off, and on unload.
    private void GoOffline()
    {
        if (!api.IsConfigured) { activity.Stop(); return; }
        activity.Tick();
        activity.Stop();
        emotes.Flush();
        var snapshot = new SnapshotDto { Online = false, Privacy = reader.Privacy() };
        activity.Fill(snapshot);
        api.SendHeartbeat(snapshot);
        // Send a full snapshot (and location) as soon as he's back, not up to a heartbeat later.
        lastHeartbeat = DateTime.MinValue;
        location.Reset();
        lastSnapshot = null;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        ClientState.Logout -= OnLogout;
        Commands.RemoveHandler(Command);
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfig;
        windows.RemoveAllWindows();
        GoOffline(); // plugin disabled/updated or game closing; ApiClient.Dispose gives it up to 2s to go out
        playtime.Dispose();
        emotes.Dispose();
        events.Dispose();
        api.Dispose();
    }
}
