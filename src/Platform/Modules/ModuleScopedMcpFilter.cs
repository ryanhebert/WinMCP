using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Pipeline middleware that makes a single underlying MCP server appear as
/// multiple per-module servers. Enforces module-scope on call methods
/// (<c>tools/call</c>, <c>prompts/get</c>, <c>resources/read</c>) by
/// pre-checking the JSON-RPC body, and filters list-method responses
/// (<c>tools/list</c>, <c>prompts/list</c>, <c>resources/list</c>) by
/// post-processing the SDK's SSE stream.
/// </summary>
/// <remarks>
/// Why this exists: <c>ModelContextProtocol.AspNetCore</c> 1.2.0 only
/// supports a single <c>StreamableHttpHandler</c> in DI, so multiple
/// <c>MapMcp(path)</c> calls share one tool list. This shim recovers the
/// per-module URL semantics we want without forking the SDK. Tracked in
/// the backlog: when the SDK gains native multi-server support, this whole
/// middleware can be deleted.
/// </remarks>
public sealed class ModuleScopedMcpFilter
{
    private readonly RequestDelegate _next;
    private readonly ModuleRegistry _registry;
    private readonly ILogger<ModuleScopedMcpFilter> _logger;
    private readonly string _moduleName;

    public ModuleScopedMcpFilter(
        RequestDelegate next,
        ModuleRegistry registry,
        ILogger<ModuleScopedMcpFilter> logger,
        string moduleName)
    {
        _next = next;
        _registry = registry;
        _logger = logger;
        _moduleName = moduleName;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only POST bodies carry JSON-RPC method calls; GET = SSE stream
        // open, DELETE = session teardown. Both pass through unmodified.
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();
        var (jsonRpcMethod, callTarget) = await PeekJsonRpcAsync(context);
        context.Request.Body.Position = 0;

        // Pre-call gate for tool/prompt/resource invocations.
        if (callTarget is not null && !IsCallTargetInModule(jsonRpcMethod!, callTarget))
        {
            _logger.LogWarning(
                "Cross-module call rejected: module={Module} method={Method} target={Target}",
                _moduleName, jsonRpcMethod, callTarget);
            await WriteJsonRpcErrorAsync(context, jsonRpcMethod!, callTarget);
            return;
        }

        // For list-method requests we need to filter the response. Buffer
        // the response body, let the SDK write into it, then post-process.
        var isListMethod = IsListMethod(jsonRpcMethod);
        if (!isListMethod)
        {
            await _next(context);
            return;
        }

        var origBody = context.Response.Body;
        await using var captured = new MemoryStream();
        context.Response.Body = captured;

        try
        {
            await _next(context);
        }
        finally
        {
            captured.Position = 0;
            var rewritten = FilterListResponse(captured, jsonRpcMethod!);
            context.Response.Body = origBody;
            if (rewritten is not null)
            {
                context.Response.ContentLength = rewritten.Length;
                await context.Response.Body.WriteAsync(rewritten);
            }
            else
            {
                // Couldn't parse — pass the original through. Better to leak
                // tool names than 500 the request.
                captured.Position = 0;
                await captured.CopyToAsync(context.Response.Body);
            }
        }
    }

    private bool IsCallTargetInModule(string method, string target) => method switch
    {
        "tools/call" => _registry.ToolBelongsTo(target, _moduleName),
        "prompts/get" => _registry.PromptBelongsTo(target, _moduleName),
        "resources/read" => _registry.ResourceBelongsTo(target, _moduleName),
        _ => true
    };

    private static bool IsListMethod(string? method) => method is
        "tools/list" or "prompts/list" or "resources/list";

    private static async Task<(string? Method, string? CallTarget)> PeekJsonRpcAsync(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0) return (null, null);
        var ct = context.Request.ContentType ?? "";
        if (!ct.Contains("json", StringComparison.OrdinalIgnoreCase)) return (null, null);

        try
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            var method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;

            string? target = null;
            if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                target = method switch
                {
                    "tools/call" or "prompts/get" =>
                        p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
                    "resources/read" =>
                        p.TryGetProperty("uri", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null,
                    _ => null
                };
            }

            return (method, target);
        }
        catch
        {
            return (null, null);
        }
    }

    private byte[]? FilterListResponse(Stream captured, string listMethod)
    {
        // SDK emits SSE: zero or more `event: message\ndata: {...}\n\n`
        // entries. For list methods there's typically exactly one data
        // line carrying the JSON-RPC response.
        captured.Position = 0;
        using var reader = new StreamReader(captured, Encoding.UTF8, leaveOpen: true);
        var raw = reader.ReadToEnd();

        var sb = new StringBuilder();
        foreach (var line in raw.Split('\n'))
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                sb.Append(line).Append('\n');
                continue;
            }
            var json = line.AsSpan("data:".Length).Trim();
            if (json.IsEmpty)
            {
                sb.Append(line).Append('\n');
                continue;
            }

            var filtered = TryFilterDataJson(json.ToString(), listMethod);
            if (filtered is null) return null;
            sb.Append("data: ").Append(filtered).Append('\n');
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private string? TryFilterDataJson(string json, string listMethod)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                return json;
            }

            var arrayKey = listMethod switch
            {
                "tools/list" => "tools",
                "prompts/list" => "prompts",
                "resources/list" => "resources",
                _ => null
            };
            if (arrayKey is null) return json;

            if (!result.TryGetProperty(arrayKey, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return json;
            }

            var keepIndices = new List<int>();
            var idx = 0;
            foreach (var item in arr.EnumerateArray())
            {
                if (BelongsToThisModule(arrayKey, item)) keepIndices.Add(idx);
                idx++;
            }

            // Rebuild the JSON with only the kept items.
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                WriteRebuilt(w, root, arrayKey, keepIndices, arr);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private bool BelongsToThisModule(string arrayKey, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return false;
        return arrayKey switch
        {
            "tools" => item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String &&
                       _registry.ToolBelongsTo(n.GetString()!, _moduleName),
            "prompts" => item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String &&
                        _registry.PromptBelongsTo(n.GetString()!, _moduleName),
            "resources" => (item.TryGetProperty("uri", out var u) && u.ValueKind == JsonValueKind.String &&
                            _registry.ResourceBelongsTo(u.GetString()!, _moduleName)) ||
                           (item.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String &&
                            _registry.ResourceBelongsTo(nm.GetString()!, _moduleName)),
            _ => false
        };
    }

    private static void WriteRebuilt(
        Utf8JsonWriter w, JsonElement root, string arrayKey, List<int> keepIndices, JsonElement originalArray)
    {
        w.WriteStartObject();
        foreach (var topProp in root.EnumerateObject())
        {
            if (topProp.NameEquals("result"))
            {
                w.WritePropertyName("result");
                w.WriteStartObject();
                foreach (var rp in topProp.Value.EnumerateObject())
                {
                    if (rp.NameEquals(arrayKey))
                    {
                        w.WritePropertyName(arrayKey);
                        w.WriteStartArray();
                        var i = 0;
                        foreach (var item in originalArray.EnumerateArray())
                        {
                            if (keepIndices.Contains(i)) item.WriteTo(w);
                            i++;
                        }
                        w.WriteEndArray();
                    }
                    else if (rp.NameEquals("nextCursor"))
                    {
                        // Cursor would reference the unfiltered list; drop
                        // it. See BACKLOG — cursor correctness deferred.
                    }
                    else
                    {
                        rp.WriteTo(w);
                    }
                }
                w.WriteEndObject();
            }
            else
            {
                topProp.WriteTo(w);
            }
        }
        w.WriteEndObject();
    }

    private async Task WriteJsonRpcErrorAsync(HttpContext context, string method, string target)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";
        var body = new
        {
            jsonrpc = "2.0",
            id = (string?)null,
            error = new
            {
                code = -32601,
                message = $"'{target}' is not a {ItemKindFor(method)} in module '{_moduleName}'.",
            }
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }

    private static string ItemKindFor(string method) => method switch
    {
        "tools/call" => "tool",
        "prompts/get" => "prompt",
        "resources/read" => "resource",
        _ => "item"
    };
}
