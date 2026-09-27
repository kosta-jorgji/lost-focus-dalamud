using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LostFocus.Api;

namespace LostFocus.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly ApiClient api;
    private readonly Action sendNow;

    public ConfigWindow(Configuration config, ApiClient api, Action sendNow)
        : base("Lost Focus Tracker###LostFocusConfig")
    {
        this.config = config;
        this.api = api;
        this.sendNow = sendNow;
        Size = new Vector2(420, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var dirty = false;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled)) { config.Enabled = enabled; dirty = true; }
        ImGui.SameLine();
        ImGui.TextDisabled(api.IsConfigured
            ? api.LastError != null ? $"last error: {api.LastError}"
            : api.LastSuccess == DateTime.MinValue ? "not sent yet"
            : $"last ok {(DateTime.UtcNow - api.LastSuccess).TotalSeconds:0}s ago"
            : "not configured");

        ImGui.Spacing();
        var url = config.ServerUrl;
        if (ImGui.InputText("Server URL", ref url, 256)) { config.ServerUrl = url; dirty = true; }
        var secret = config.Secret;
        if (ImGui.InputText("Secret", ref secret, 128, ImGuiInputTextFlags.Password)) { config.Secret = secret; dirty = true; }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("What the site is allowed to know");
        ImGui.TextDisabled("Off means it is never read from the game, let alone sent.");
        ImGui.Spacing();

        var privacy = false;

        privacy |= Toggle("Online status & playtime", () => config.SharePlaytime, v => config.SharePlaytime = v,
            "Whether you're logged in, session length, hours per week.");
        privacy |= Toggle("Zone, duty & pulls", () => config.ShareZone, v => config.ShareZone = v,
            "Where you are, what duty, in combat, pull count.");
        privacy |= Toggle("Deaths & wipes", () => config.ShareDeaths, v => config.ShareDeaths = v,
            "Every death, how long you stayed on the floor, every wipe.");
        privacy |= Toggle("Job, level & item level", () => config.ShareGear, v => config.ShareGear = v, null);
        privacy |= Toggle("PvP record", () => config.SharePvp, v => config.SharePvp = v,
            "Frontline placements, Crystalline Conflict rank, series level.");
        privacy |= Toggle("Emote counts", () => config.ShareEmotes, v => config.ShareEmotes = v,
            "How many times you've used each emote. Only the totals, never who you did it at.");

        // Don't make him wait for the next heartbeat for the site to drop something he just turned off.
        if (privacy)
        {
            dirty = true;
            sendNow();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled($"Sends an update every {Plugin.HeartbeatSeconds}s.");
        if (ImGui.Button("Send now")) sendNow();

        if (dirty) config.Save();
    }

    private static bool Toggle(string label, Func<bool> get, Action<bool> set, string? help)
    {
        var v = get();
        var changed = ImGui.Checkbox(label, ref v);
        if (changed) set(v);
        if (help != null && ImGui.IsItemHovered()) ImGui.SetTooltip(help);
        return changed;
    }
}
