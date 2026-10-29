using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace LostFocus.Api;

/// <summary>
/// Queues POSTs to the server and sends them on a background task.
/// Failures are logged and dropped, not retried.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly BlockingCollection<(string path, object body)> queue = new(64);
    private readonly CancellationTokenSource cts = new();
    private readonly Task worker;

    public DateTime LastSuccess { get; private set; } = DateTime.MinValue;
    public string? LastError { get; private set; }

    public ApiClient(Configuration config, IPluginLog log)
    {
        this.config = config;
        this.log = log;
        worker = Task.Run(Loop);
    }

    public bool IsConfigured =>
        config.Enabled && Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out _) && config.Secret.Length > 0;

    public void SendHeartbeat(SnapshotDto snapshot) => Enqueue("/plugin/heartbeat", snapshot);
    public void SendEvent(EventDto ev) => Enqueue("/plugin/event", ev);

    // At most one location request is queued; it sends the latest location when dequeued.
    private const string LocationPath = "/plugin/location";
    private static readonly object LocationMarker = new();
    private LocationDto? latestLocation;
    private int locationQueued;

    public void SendLocation(LocationDto location)
    {
        Volatile.Write(ref latestLocation, location);
        if (Interlocked.Exchange(ref locationQueued, 1) == 1) return; // one is already waiting
        if (!Enqueue(LocationPath, LocationMarker)) Volatile.Write(ref locationQueued, 0);
    }

    private bool Enqueue(string path, object body)
    {
        if (!IsConfigured) return false;
        if (queue.TryAdd((path, body))) return true;
        log.Warning("[api] queue full, dropping {Path}", path);
        return false;
    }

    private async Task Loop()
    {
        foreach (var (path, queued) in queue.GetConsumingEnumerable(cts.Token))
        {
            var body = queued;
            if (ReferenceEquals(body, LocationMarker))
            {
                Volatile.Write(ref locationQueued, 0);
                var latest = Interlocked.Exchange(ref latestLocation, null);
                if (latest == null) continue;
                body = latest;
            }

            try
            {
                var url = config.ServerUrl.TrimEnd('/') + path;
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(body, body.GetType(), options: JsonOpts),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Secret);
                using var res = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    LastSuccess = DateTime.UtcNow;
                    LastError = null;
                }
                else
                {
                    LastError = $"{(int)res.StatusCode} from {path}";
                    log.Warning("[api] {Path} -> {Status}", path, (int)res.StatusCode);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                log.Warning(ex, "[api] {Path} failed", path);
            }
        }
    }

    public void Dispose()
    {
        // Give queued requests up to 2s to send, then cancel the rest.
        queue.CompleteAdding();
        try { worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        cts.Cancel();
        try { worker.Wait(TimeSpan.FromSeconds(1)); } catch { /* shutting down */ }
        http.Dispose();
        cts.Dispose();
        queue.Dispose();
    }
}
