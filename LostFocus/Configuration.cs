using System;
using Dalamud.Configuration;

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

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
