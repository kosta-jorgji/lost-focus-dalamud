using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using LostFocus.Api;

namespace LostFocus.Tracking;

public sealed class ActivityTracker
{
    private readonly Configuration config;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly ActivityTimers timers;
    private long lastTick = Stopwatch.GetTimestamp();

    public ActivityTracker(Configuration config, IClientState clientState, IObjectTable objects, ICondition condition)
    {
        this.config = config;
        this.clientState = clientState;
        this.objects = objects;
        this.condition = condition;
        timers = new ActivityTimers(config.JobTimes, config.MountTimes);
    }

    public bool Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds;
        lastTick = now;
        var player = objects.LocalPlayer;
        var available = clientState.IsLoggedIn && player != null
            && !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.BetweenAreas51];
        // Only read opted-in categories. CurrentMount is Dalamud's supported mount row accessor.
        var job = available && config.ShareJobTime ? player!.ClassJob.ValueNullable?.Abbreviation.ExtractText() : null;
        var mount = available && config.ShareMountTime ? player!.CurrentMount : null;
        var changed = timers.Update(elapsed, available, job, mount?.RowId ?? 0,
            mount?.ValueNullable?.Singular.ExtractText(), config.ShareJobTime, config.ShareMountTime);
        return changed && (config.ShareJobTime || config.ShareMountTime);
    }

    public void Fill(SnapshotDto snapshot)
    {
        if (config.ShareJobTime) snapshot.JobActivity = timers.ReadJobs();
        if (config.ShareMountTime) snapshot.MountActivity = timers.ReadMounts();
        if (config.ShareJobTime || config.ShareMountTime) config.Save();
    }

    public void Stop()
    {
        timers.Stop();
        lastTick = Stopwatch.GetTimestamp();
    }
}
