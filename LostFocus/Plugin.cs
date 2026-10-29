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

    /// Interval between heartbeats and emote flushes.
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

    /// Sends a full snapshot immediately and resends the location.
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

        // Logged out: GoOffline already reported it.
        if (!ClientState.IsLoggedIn) return;
        if (Objects.LocalPlayer == null)
        {
            // Loading screen: nothing to read, so resend the last snapshot with the activity timers paused.
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

    /// Stops the timers, flushes emotes and sends an offline snapshot.
    private void GoOffline()
    {
        if (!api.IsConfigured) { activity.Stop(); return; }
        activity.Tick();
        activity.Stop();
        emotes.Flush();
        var snapshot = new SnapshotDto { Online = false, Privacy = reader.Privacy() };
        activity.Fill(snapshot);
        api.SendHeartbeat(snapshot);
        // Send a full snapshot and location on the next tick after login.
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
        GoOffline(); // ApiClient.Dispose gives it up to 2s to send
        playtime.Dispose();
        emotes.Dispose();
        events.Dispose();
        api.Dispose();
    }
}
