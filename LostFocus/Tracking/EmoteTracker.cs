using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using LostFocus.Api;

namespace LostFocus.Tracking;

/// <summary>
/// Counts the emotes he uses. Hooks the local player's emote entry point, so text
/// commands, the emote window and hotbars all land here. Counts are batched and
/// sent at most once per heartbeat so /dance spam doesn't turn into request spam.
/// </summary>
public sealed unsafe class EmoteTracker : IDisposable
{
    private delegate bool ExecuteEmoteDelegate(EmoteManager* self, ushort emoteId, EmoteController.PlayEmoteOption* option);

    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly GameStateReader reader;
    private readonly ApiClient api;
    private readonly Hook<ExecuteEmoteDelegate>? hook;

    private readonly Dictionary<string, int> pending = new();
    private DateTime lastFlush = DateTime.UtcNow;

    public EmoteTracker(IGameInteropProvider interop, IPluginLog log, Configuration config, GameStateReader reader, ApiClient api)
    {
        this.log = log;
        this.config = config;
        this.reader = reader;
        this.api = api;

        try
        {
            hook = interop.HookFromAddress<ExecuteEmoteDelegate>((nint)EmoteManager.MemberFunctionPointers.ExecuteEmote, Detour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            // A game patch moved it; everything else keeps working.
            log.Error(ex, "[emotes] failed to hook ExecuteEmote, emote tracking disabled");
        }
    }

    private bool Detour(EmoteManager* self, ushort emoteId, EmoteController.PlayEmoteOption* option)
    {
        var ok = hook!.Original(self, emoteId, option);
        try
        {
            if (ok && config.ShareEmotes)
            {
                var name = reader.EmoteName(emoteId);
                if (name != null) pending[name] = pending.GetValueOrDefault(name) + 1;
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[emotes] failed to record emote {Id}", emoteId);
        }
        return ok;
    }

    /// Called every framework tick.
    public void Tick()
    {
        if (DateTime.UtcNow - lastFlush >= TimeSpan.FromSeconds(config.HeartbeatSeconds)) Flush();
    }

    public void Flush()
    {
        lastFlush = DateTime.UtcNow;
        if (pending.Count == 0) return;
        if (config.ShareEmotes)
        {
            var data = new Dictionary<string, object?>();
            foreach (var (name, n) in pending) data[name] = n;
            api.SendEvent(new EventDto { Type = "emotes", At = DateTime.UtcNow.ToString("o"), Data = data });
        }
        pending.Clear();
    }

    public void Dispose() => hook?.Dispose();
}
