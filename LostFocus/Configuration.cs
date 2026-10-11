using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using LostFocus.Api;

namespace LostFocus;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = false;
    public string ServerUrl { get; set; } = "";
    public string Secret { get; set; } = "";

    // Privacy toggles. Everything he turns off is never sent - not just hidden.
    public bool SharePlaytime { get; set; } = true;
    public bool ShareZone { get; set; } = true;
    public bool ShareDeaths { get; set; } = true;
    public bool ShareGear { get; set; } = true;
    public bool SharePvp { get; set; } = true;
    public bool ShareEmotes { get; set; } = true;
    // Off by default: this lets anyone on the site find him in-game.
    public bool ShareLocation { get; set; } = false;
    public int LocationSeconds { get; set; } = 5;

    public bool ShareJobTime { get; set; } = true;
    public bool ShareMountTime { get; set; } = true;
    // Cumulative readings survive reloads and can be resent safely after a missed heartbeat.
    public Dictionary<string, JobTimeDto> JobTimes { get; set; } = new();
    public Dictionary<uint, MountTimeDto> MountTimes { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
