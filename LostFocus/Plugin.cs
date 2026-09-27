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
    private readonly WindowSystem windows = new("LostFocus");
    private readonly ConfigWindow configWindow;

    private DateTime lastHeartbeat = DateTime.MinValue;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        api = new ApiClient(config, Log);
        reader = new GameStateReader(ClientState, Objects, Condition, DataManager, config);
        events = new EventTracker(ClientState, Objects, DutyState, Log, config, reader, api);
        emotes = new EmoteTracker(Interop, Log, config, reader, api);

        configWindow = new ConfigWindow(config, api, () => SendHeartbeat(force: true), GoOffline);
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
        if (!api.IsConfigured) return;
        events.Tick();
        emotes.Tick();
        SendHeartbeat(force: false);
    }

    private void SendHeartbeat(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now - lastHeartbeat < TimeSpan.FromSeconds(HeartbeatSeconds)) return;

        // Only while he's actually in the game. Title screen / character select: logout already told
        // the site he's offline. Loading screen: there's no player object, so every field would read
        // as empty and the site would flash offline; keep the last snapshot until he's back.
        if (!ClientState.IsLoggedIn || Objects.LocalPlayer == null) return;

        api.SendHeartbeat(reader.Read(events.PullNumber));
        lastHeartbeat = now;
    }

    private void OnLogout(int type, int code) => GoOffline();

    /// Explicit offline snapshot so the site flips to offline immediately instead of waiting out the
    /// backend's stale timeout (~2.5 min). Used on logout, when tracking is switched off, and on unload.
    private void GoOffline()
    {
        if (!api.IsConfigured) return;
        emotes.Flush();
        api.SendHeartbeat(new SnapshotDto { Online = false, Privacy = reader.Privacy() });
        // Send a full snapshot as soon as he's back, not up to a minute later.
        lastHeartbeat = DateTime.MinValue;
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
        emotes.Dispose();
        events.Dispose();
        api.Dispose();
    }
}
