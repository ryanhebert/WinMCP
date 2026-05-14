using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WinMcp.Platform;

public sealed record RequestLogEntry(
    string TimestampIso,
    string Module,
    string Method,
    string Args,
    int Status,
    int DurationMs,
    string Host,
    string RemoteIp);

/// <summary>
/// In-memory ring buffer of recent MCP requests, exposed via the dashboard
/// and <c>/requests</c>. Ported from math-mcp v1.0.21; thread-safe via
/// <see cref="ConcurrentQueue{T}"/> + a single trim lock.
/// </summary>
public sealed class RequestLog
{
    private const int Capacity = 50;
    private readonly ConcurrentQueue<RequestLogEntry> _entries = new();
    private readonly object _trimLock = new();

    public void Record(RequestLogEntry entry)
    {
        _entries.Enqueue(entry);
        if (_entries.Count > Capacity)
        {
            lock (_trimLock)
            {
                while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
            }
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<RequestLogEntry> Snapshot()
    {
        var list = _entries.ToArray();
        Array.Reverse(list);
        return list;
    }
}

/// <summary>
/// Per-request middleware that captures one row per <c>/&lt;module&gt;/mcp</c>
/// hit into the ring buffer and emits a structured Serilog INFO line.
/// Carries forward v1.0.23's "session not found" annotation and v1.0.24's
/// prefix-aware bearer awareness (foreign-shaped bearers don't change the
/// labelling here — that's the validator's concern).
/// </summary>
public sealed class RequestLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestLog _log;
    private readonly ILogger<RequestLogMiddleware> _logger;
    private readonly string _moduleName;

    public RequestLogMiddleware(
        RequestDelegate next,
        RequestLog log,
        ILogger<RequestLogMiddleware> logger,
        string moduleName)
    {
        _next = next;
        _log = log;
        _logger = logger;
        _moduleName = moduleName;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var ts = DateTime.UtcNow;
        var httpMethod = context.Request.Method;
        var pathStr = context.Request.Path.Value ?? $"/{_moduleName}/mcp";

        context.Request.EnableBuffering();
        var (jsonRpcMethod, args) = await TryParseJsonRpc(context);
        context.Request.Body.Position = 0;

        var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();
        var hasSessionId = !string.IsNullOrEmpty(sessionId);

        await _next(context);
        sw.Stop();

        var status = context.Response.StatusCode;
        string method;
        if (!string.IsNullOrEmpty(jsonRpcMethod))
        {
            method = jsonRpcMethod;
        }
        else if (status == StatusCodes.Status401Unauthorized)
        {
            method = "(unauthenticated)";
        }
        else
        {
            method = $"{httpMethod} {pathStr}";
        }

        if (status == StatusCodes.Status404NotFound && hasSessionId && string.IsNullOrEmpty(jsonRpcMethod))
        {
            args = "session not found (stale Mcp-Session-Id)";
        }

        var host = context.Request.Host.Value ?? "";
        var remoteIp = ResolveRemoteIp(context);
        var durationMs = (int)sw.ElapsedMilliseconds;

        _log.Record(new RequestLogEntry(
            TimestampIso: ts.ToString("O"),
            Module: _moduleName,
            Method: method,
            Args: args,
            Status: status,
            DurationMs: durationMs,
            Host: host,
            RemoteIp: remoteIp));

        _logger.LogInformation(
            "MCP[{Module}] {Method} {Args} host={Host} ip={RemoteIp} status={Status} dur={DurationMs}ms",
            _moduleName, method, args, host, remoteIp, status, durationMs);
    }

    private static string ResolveRemoteIp(HttpContext context)
    {
        var xff = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(xff))
        {
            var first = xff.Split(',', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "-";
    }

    private static async Task<(string Method, string Args)> TryParseJsonRpc(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0) return ("", "—");
        if (!(context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return ("", "—");
        }

        try
        {
            using var doc = await JsonDocument.ParseAsync(
                context.Request.Body, cancellationToken: context.RequestAborted);

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ("", "—");

            var method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? ""
                : "";

            var args = "—";
            if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                if (method == "tools/call" &&
                    p.TryGetProperty("name", out var name) &&
                    name.ValueKind == JsonValueKind.String)
                {
                    var tool = name.GetString() ?? "";
                    if (p.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                    {
                        var parts = new List<string>();
                        foreach (var prop in a.EnumerateObject())
                        {
                            parts.Add(prop.Value.ToString());
                        }
                        args = $"{tool}({string.Join(", ", parts)})";
                    }
                    else
                    {
                        args = $"{tool}()";
                    }
                }
            }

            return (method, args);
        }
        catch
        {
            return ("", "—");
        }
    }
}
