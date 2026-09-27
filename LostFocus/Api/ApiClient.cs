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
/// Fire-and-forget HTTP to the backend. Requests are queued and sent on a
/// background task so nothing blocks the game thread; failures are logged and
/// dropped (a missed heartbeat is not worth retrying).
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

    private void Enqueue(string path, object body)
    {
        if (!IsConfigured) return;
        if (!queue.TryAdd((path, body)))
            log.Warning("[api] queue full, dropping {Path}", path);
    }

    private async Task Loop()
    {
        foreach (var (path, body) in queue.GetConsumingEnumerable(cts.Token))
        {
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
        // Let whatever is queued (last emote batch, offline heartbeat) go out, but don't hold up
        // the unload for more than 2s if the server is slow; then cancel the rest.
        queue.CompleteAdding();
        try { worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        cts.Cancel();
        try { worker.Wait(TimeSpan.FromSeconds(1)); } catch { /* shutting down */ }
        http.Dispose();
        cts.Dispose();
        queue.Dispose();
    }
}
