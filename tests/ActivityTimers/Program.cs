using LostFocus.Api;
using LostFocus.Tracking;

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{message}: expected {expected}, got {actual}");
}

var jobs = new Dictionary<string, JobTimeDto>();
var mounts = new Dictionary<uint, MountTimeDto>();
var timer = new ActivityTimers(jobs, mounts);
timer.Update(0, true, "SGE", 1, "Company chocobo", true, true);
timer.Update(2, true, "SGE", 1, "Company chocobo", true, true);
var queuedCopy = timer.ReadMounts();
Equal(2d, jobs["SGE"].Seconds, "initial job time");
Equal(1, mounts[1].Rides, "first ride");

timer.Update(1, false, null, 0, null, true, true);
Equal(false, timer.ReadMounts().Current!.Running, "loading pauses the live timer");
timer.Update(100, true, "SGE", 1, "Company chocobo", true, true);
Equal(2d, mounts[1].Seconds, "loading excluded");
Equal(1, mounts[1].Rides, "loading does not create another ride");

timer.Update(3, true, "WAR", 14, "Fat chocobo", true, true);
Equal(5d, jobs["SGE"].Seconds, "job change finishes previous streak");
Equal(5d, mounts[1].LongestRideSeconds, "longest ride includes both sides of loading");
Equal(2d, queuedCopy.Totals[0].Seconds, "queued payload cannot mutate");
timer.Update(4, true, "WAR", 0, null, true, true);
Equal(4d, mounts[14].Seconds, "dismount finishes ride");
Equal(true, timer.ReadMounts().Current == null, "no live mount after dismount");

timer.Update(1, true, "WAR", 1, "Company chocobo", true, true);
timer.Update(2, true, "WAR", 1, "Company chocobo", true, true);
Equal(7d, mounts[1].Seconds, "later rides append time");
Equal(2, mounts[1].Rides, "later rides increment count");
Equal(5d, mounts[1].LongestRideSeconds, "shorter ride does not replace record");

timer.Update(1000, true, "WAR", 1, "Company chocobo", true, true);
Equal(7d, mounts[1].Seconds, "suspended game excludes unobserved time");
timer.Update(2, true, null, 0, null, false, false);
Equal(7d, mounts[1].Seconds, "disabled category does not accrue time");
Equal(true, timer.ReadJobs().Current == null, "disabled category clears live timer");
timer.Stop();
timer.Update(5000, true, "SGE", 1, "Company chocobo", true, true);
Equal(7d, mounts[1].Seconds, "offline time excluded");
Equal(0d, timer.ReadJobs().Current!.Seconds, "login starts a new stint");
Equal(3, mounts[1].Rides, "new observed ride after stop");

timer.Update(1, true, "SGE", 2, "", true, true);
timer.Update(2, true, "SGE", 2, "", true, true);
Equal(false, mounts.ContainsKey(2), "unnamed quest/duty mount is ignored");
Equal(true, timer.ReadMounts().Current == null, "unnamed mount counts as on foot");
Equal(8d, mounts[1].Seconds, "switching to an unnamed mount ends the ride");

var sgeBefore = jobs["SGE"].Seconds;
timer.Update(1, true, "CRP", 0, null, true, true);
timer.Update(3, true, "CRP", 0, null, true, true);
Equal(false, jobs.ContainsKey("CRP"), "crafters are not timed");
Equal(true, timer.ReadJobs().Current == null, "crafting ends the streak");
Equal(sgeBefore + 1, jobs["SGE"].Seconds, "crafting time is not added to the previous job");
timer.Update(1, true, "SGE", 0, null, true, true);
Equal(0d, timer.ReadJobs().Current!.Seconds, "back from crafting starts a new streak");
Equal(sgeBefore + 1, jobs["SGE"].Seconds, "crafting time still not added after returning");
jobs["BTN"] = new JobTimeDto { Job = "BTN", Seconds = 60 };

// Reloading the accumulator keeps the durable counters.
var reloaded = new ActivityTimers(jobs, mounts);
Equal(8d, reloaded.ReadMounts().Totals.Single(r => r.Id == 1).Seconds, "reload keeps totals");
Equal(false, jobs.ContainsKey("BTN"), "reload drops gatherer time recorded before the filter");
Console.WriteLine("Activity timer checks passed (job changes, ride accumulation, unnamed mounts, DoH/DoL, loading, privacy, offline, snapshots, reload).");
