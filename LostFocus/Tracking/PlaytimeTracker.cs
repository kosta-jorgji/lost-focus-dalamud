using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using LostFocus.Api;

namespace LostFocus.Tracking;

/// <summary>
/// His real total playtime, from the game's own /playtime. Asked once per login, a few seconds
/// after he's in (the reply that triggers is kept out of his chat), and also picked up whenever
/// he types /playtime himself. The backend adds tracked session time on top until the next reading.
/// </summary>
public sealed unsafe class PlaytimeTracker : IDisposable
{
    /// LogMessage row "Total Play Time: [days] [hours] [minutes]"; its three int parameters.
    private const uint PlaytimeLogMessage = 859;
    private static readonly TimeSpan AskAfter = TimeSpan.FromSeconds(10);
    /// A reply this soon after asking is ours, so it's hidden.
    private static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(15);
    /// Fallback if the log message hook doesn't see it: the printed English line.
    private static readonly Regex PlaytimeLine = new(
        @"^Total Play Time: (?:(\d+) days?, )?(\d+) hours?, (\d+) minutes?", RegexOptions.CultureInvariant);

    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly ApiClient api;

    private bool asked;
    private DateTime? readySince;
    private DateTime askedAt = DateTime.MinValue;
    private DateTime lastReading = DateTime.MinValue;

    public PlaytimeTracker(
        IClientState clientState, IObjectTable objects, ICondition condition, IChatGui chat, IPluginLog log,
        Configuration config, ApiClient api)
    {
        this.clientState = clientState;
        this.objects = objects;
        this.condition = condition;
        this.chat = chat;
        this.log = log;
        this.config = config;
        this.api = api;

        chat.LogMessage += OnLogMessage;
        chat.ChatMessage += OnChatMessage;
        clientState.Logout += OnLogout;
    }

    /// Called every framework tick. Waits until he's been in the world, out of loading screens
    /// and cutscenes, for a few seconds before asking.
    public void Tick()
    {
        if (asked || !config.SharePlaytime) return;
        var busy = !clientState.IsLoggedIn || objects.LocalPlayer == null
            || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51]
            || condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene];
        if (busy)
        {
            readySince = null;
            return;
        }

        var now = DateTime.UtcNow;
        readySince ??= now;
        if (now - readySince < AskAfter) return;

        // A null UI module would be an access violation, which no catch can stop; skip until it exists.
        var ui = UIModule.Instance();
        if (ui == null) return;

        asked = true;
        askedAt = now;
        var command = Utf8String.FromString("/playtime");
        try
        {
            ui->ProcessChatBoxEntry(command);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[playtime] failed to ask for /playtime");
        }
        finally
        {
            command->Dtor(true); // native string; frees it and itself
        }
    }

    private void OnLogMessage(ILogMessage message)
    {
        if (message.LogMessageId != PlaytimeLogMessage) return;
        if (!message.TryGetIntParameter(0, out var days) || !message.TryGetIntParameter(1, out var hours)
            || !message.TryGetIntParameter(2, out var minutes)) return;
        Record(days, hours, minutes);
        if (DateTime.UtcNow - askedAt < QuietFor) message.PreventOriginal();
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // Every chat line comes through here; only system lines can be it, so don't decode the rest.
        if (message.LogKind != XivChatType.SystemMessage) return;
        if (DateTime.UtcNow - lastReading < TimeSpan.FromSeconds(5)) return; // the log message already had it
        var match = PlaytimeLine.Match(message.Message.TextValue);
        if (!match.Success) return;
        var days = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        Record(days, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        if (DateTime.UtcNow - askedAt < QuietFor) message.PreventOriginal();
    }

    private void Record(int days, int hours, int minutes)
    {
        lastReading = DateTime.UtcNow;
        if (!config.SharePlaytime) return;
        var total = (days * 24 + hours) * 60 + minutes;
        log.Debug("[playtime] {Minutes} min", total);
        api.SendEvent(new EventDto
        {
            Type = "playtime",
            At = DateTime.UtcNow.ToString("o"),
            Data = new Dictionary<string, object?> { ["minutes"] = total },
        });
    }

    private void OnLogout(int type, int code)
    {
        asked = false;
        readySince = null;
    }

    public void Dispose()
    {
        chat.LogMessage -= OnLogMessage;
        chat.ChatMessage -= OnChatMessage;
        clientState.Logout -= OnLogout;
    }
}
