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

    private readonly Configuration config;
    private readonly ApiClient api;
    private readonly GameStateReader reader;
    private readonly EventTracker events;
    private readonly EmoteTracker emotes;
    private readonly WindowSystem windows = new("LostFocus");
    private readonly ConfigWindow configWindow;

    private DateTime lastHeartbeat = DateTime.MinValue;
    private DateTime lastChangeSend = DateTime.MinValue;
    private DateTime lastCheck = DateTime.MinValue;
    private string lastFingerprint = "";

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        api = new ApiClient(config, Log);
        reader = new GameStateReader(ClientState, Objects, Condition, DataManager, config);
        events = new EventTracker(ClientState, Objects, DutyState, Log, config, reader, api);
        emotes = new EmoteTracker(Interop, Log, config, reader, api);

        configWindow = new ConfigWindow(config, api, () => SendHeartbeat(force: true));
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
        var due = now - lastHeartbeat >= TimeSpan.FromSeconds(config.HeartbeatSeconds);
        // Reading the game state isn't free (gear, PvP profile, sheet lookups), so look for changes
        // at most once a second, and send at most one change per 2s (job swaps, combat flicker).
        var mayCheckChange = now - lastCheck >= TimeSpan.FromSeconds(1) && now - lastChangeSend >= TimeSpan.FromSeconds(2);
        if (!force && !due && !mayCheckChange) return;
        lastCheck = now;

        // Loading screen: still logged in but there's no player object, so every field would read
        // as empty and the site would flash offline. Keep the last snapshot until he's back.
        if (ClientState.IsLoggedIn && Objects.LocalPlayer == null) return;

        var snap = reader.Read(events.PullNumber);
        var fp = snap.Fingerprint();
        var changed = fp != lastFingerprint;
        if (!force && !due && !changed) return;

        api.SendHeartbeat(snap);
        lastFingerprint = fp;
        lastHeartbeat = now;
        if (changed) lastChangeSend = now;
    }

    private void OnLogout(int type, int code)
    {
        // Explicit offline snapshot so the site flips to NO immediately instead of after the stale timeout.
        if (!api.IsConfigured) return;
        emotes.Flush();
        api.SendHeartbeat(new SnapshotDto { Online = false, Privacy = reader.Privacy() });
        lastFingerprint = "";
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
        emotes.Flush();
        emotes.Dispose();
        events.Dispose();
        api.Dispose();
    }
}
