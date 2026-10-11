using System;
using System.Collections.Generic;
using System.Linq;
using LostFocus.Api;

namespace LostFocus.Tracking;

/// <summary>Accumulates observed online time. Loading pauses a job streak/ride without starting another.</summary>
public sealed class ActivityTimers
{
    /// Crafters and gatherers aren't timed. Same list the site uses to hide his whereabouts.
    private static readonly HashSet<string> DohDol = new() { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH" };

    private readonly Dictionary<string, JobTimeDto> jobs;
    private readonly Dictionary<uint, MountTimeDto> mounts;
    private string? currentJob;
    private uint currentMount;
    private double jobSeconds;
    private double mountSeconds;
    private bool running;

    public ActivityTimers(Dictionary<string, JobTimeDto> jobs, Dictionary<uint, MountTimeDto> mounts)
    {
        this.jobs = jobs;
        this.mounts = mounts;
        foreach (var job in DohDol) jobs.Remove(job);
    }

    /// Returns true on a job change, mount/dismount or pause/resume, so the site updates immediately.
    public bool Update(double elapsed, bool available, string? job, uint mountId, string? mountName, bool shareJobs, bool shareMounts)
    {
        // Unnamed rows are quest/duty vehicles, not his mounts: treat them as being on foot.
        if (string.IsNullOrEmpty(mountName)) mountId = 0;
        // A long framework stall/suspended computer is unobserved time, not a giant ride.
        if (running && available && elapsed >= 0 && elapsed <= 5)
        {
            if (shareJobs && currentJob != null)
            {
                var row = jobs[currentJob];
                row.Seconds += elapsed;
                jobSeconds += elapsed;
                row.LongestStintSeconds = Math.Max(row.LongestStintSeconds, jobSeconds);
            }
            if (shareMounts && currentMount != 0)
            {
                var row = mounts[currentMount];
                row.Seconds += elapsed;
                mountSeconds += elapsed;
                row.LongestRideSeconds = Math.Max(row.LongestRideSeconds, mountSeconds);
            }
        }

        var changed = running != available;
        running = available;
        if (!shareJobs) { changed |= currentJob != null; currentJob = null; jobSeconds = 0; }
        else if (available && job != null && DohDol.Contains(job))
        {
            // Crafting/gathering ends the streak and isn't timed.
            changed |= currentJob != null;
            currentJob = null;
            jobSeconds = 0;
        }
        else if (available && !string.IsNullOrEmpty(job) && currentJob != job)
        {
            currentJob = job;
            jobSeconds = 0;
            if (!jobs.ContainsKey(job)) jobs[job] = new JobTimeDto { Job = job };
            changed = true;
        }

        if (!shareMounts) { changed |= currentMount != 0; currentMount = 0; mountSeconds = 0; }
        else if (available && currentMount != mountId)
        {
            currentMount = mountId;
            mountSeconds = 0;
            if (mountId != 0)
            {
                if (!mounts.TryGetValue(mountId, out var row))
                    mounts[mountId] = row = new MountTimeDto { Id = mountId, Name = mountName! };
                row.Rides++;
            }
            changed = true;
        }
        return changed;
    }

    public void Stop()
    {
        currentJob = null;
        currentMount = 0;
        jobSeconds = mountSeconds = 0;
        running = false;
    }

    // Copies matter: the HTTP worker serializes these later while the game keeps updating totals.
    public JobActivityDto ReadJobs() => new()
    {
        Totals = jobs.Values.Select(r => new JobTimeDto { Job = r.Job, Seconds = r.Seconds, LongestStintSeconds = r.LongestStintSeconds }).ToList(),
        Current = currentJob == null ? null : new CurrentJobDto { Job = currentJob, Seconds = jobSeconds, Running = running },
    };

    public MountActivityDto ReadMounts() => new()
    {
        Totals = mounts.Values.Select(r => new MountTimeDto { Id = r.Id, Name = r.Name, Seconds = r.Seconds, Rides = r.Rides, LongestRideSeconds = r.LongestRideSeconds }).ToList(),
        Current = currentMount == 0 ? null : new CurrentMountDto { Id = currentMount, Name = mounts[currentMount].Name, Seconds = mountSeconds, Running = running },
    };
}
