using System;
using System.Diagnostics.CodeAnalysis;
using Yuniql.Extensibility;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Copied from sample project.
/// </summary>
[ExcludeFromCodeCoverage]
public class ConsoleTraceService : ITraceService
{
    /// <inheritdoc />
    public bool IsDebugEnabled { get; set; } = false;

    /// <inheritdoc />
    public bool IsTraceSensitiveData { get; set; } = false;

    /// <inheritdoc />
    public bool IsTraceToFile { get; set; } = false;

    /// <inheritdoc />
    public bool IsTraceToDirectory { get; set; } = false;

    /// <inheritdoc />
    public string TraceDirectory { get; set; }

    /// <inheritdoc />
    public void Info(string message, object payload = null)
    {
        var traceMessage =
            $"INF   {DateTime.UtcNow.ToString("o")}   {message}{Environment.NewLine}";
        Console.Write(traceMessage);
    }

    /// <inheritdoc />
    public void Error(string message, object payload = null)
    {
        var traceMessage =
            $"ERR   {DateTime.UtcNow.ToString("o")}   {message}{Environment.NewLine}";
        Console.Write(traceMessage);
    }

    /// <inheritdoc />
    public void Debug(string message, object payload = null)
    {
        if (IsDebugEnabled)
        {
            var traceMessage =
                $"DBG   {DateTime.UtcNow.ToString("o")}   {message}{Environment.NewLine}";
            Console.Write(traceMessage);
        }
    }

    /// <inheritdoc />
    public void Success(string message, object payload = null)
    {
        var traceMessage =
            $"INF   {DateTime.UtcNow.ToString("u")}   {message}{Environment.NewLine}";
        Console.Write(traceMessage);
    }

    /// <inheritdoc />
    public void Warn(string message, object payload = null)
    {
        var traceMessage =
            $"WRN   {DateTime.UtcNow.ToString("o")}   {message}{Environment.NewLine}";
        Console.Write(traceMessage);
    }
}
