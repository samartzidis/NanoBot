using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace NanoBot.Logging;

internal interface IStructuredLogLevel { }

public class StructuredLogLevel
{
    public class Trace : IStructuredLogLevel { }
    public class Debug : IStructuredLogLevel { }
    public class Warning : IStructuredLogLevel { }
    public class Error : IStructuredLogLevel { }
    public class Critical : IStructuredLogLevel { }
    public class Information : IStructuredLogLevel { }
}

/// <summary>
/// Extend Microsoft.Extensions.Logging ILogger with facades for structured logging using plain interpolated strings
/// </summary>
public static class LoggerExtensions
{
    #region Overloads
    public static void LogTrace(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Trace> handler) => logger.Log(handler);
    public static void LogTrace(this ILogger logger, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Trace> handler) => logger.Log(exception, handler);
    public static void LogTrace(this ILogger logger, EventId eventId, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Trace> handler) => logger.Log(eventId, handler);
    public static void LogTrace(this ILogger logger, EventId eventId, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Trace> handler) => logger.Log(eventId, exception, handler);

    public static void LogDebug(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Debug> handler) => logger.Log(handler);
    public static void LogDebug(this ILogger logger, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Debug> handler) => logger.Log(exception, handler);
    public static void LogDebug(this ILogger logger, EventId eventId, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Debug> handler) => logger.Log(eventId, handler);
    public static void LogDebug(this ILogger logger, EventId eventId, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Debug> handler) => logger.Log(eventId, exception, handler);

    public static void LogInformation(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Information> handler) => logger.Log(handler);
    public static void LogInformation(this ILogger logger, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Information> handler) => logger.Log(exception, handler);
    public static void LogInformation(this ILogger logger, EventId eventId, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Information> handler) => logger.Log(eventId, handler);
    public static void LogInformation(this ILogger logger, EventId eventId, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Information> handler) => logger.Log(eventId, exception, handler);

    public static void LogWarning(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Warning> handler) => logger.Log(handler);
    public static void LogWarning(this ILogger logger, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Warning> handler) => logger.Log(exception, handler);
    public static void LogWarning(this ILogger logger, EventId eventId, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Warning> handler) => logger.Log(eventId, handler);
    public static void LogWarning(this ILogger logger, EventId eventId, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Warning> handler) => logger.Log(eventId, exception, handler);

    public static void LogError(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Error> handler) => logger.Log(handler);
    public static void LogError(this ILogger logger, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Error> handler) => logger.Log(exception, handler);
    public static void LogError(this ILogger logger, EventId eventId, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Error> handler) => logger.Log(eventId, handler);
    public static void LogError(this ILogger logger, EventId eventId, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Error> handler) => logger.Log(eventId, exception, handler);

    public static void LogCritical(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Critical> handler) => logger.Log(handler);
    public static void LogCritical(this ILogger logger, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Critical> handler) => logger.Log(exception, handler);
    public static void LogCritical(this ILogger logger, EventId eventId, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Critical> handler) => logger.Log(eventId, handler);
    public static void LogCritical(this ILogger logger, EventId eventId, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<StructuredLogLevel.Critical> handler) => logger.Log(eventId, exception, handler);
    #endregion

    #region InternalHelpers
    internal static void Log<T>(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<T> handler) where T : IStructuredLogLevel
    {
        var (template, arguments) = handler.GetTemplateAndArguments();
        logger.Log(Enum.Parse<LogLevel>(typeof(T).Name), template, arguments);
    }

    internal static void Log<T>(this ILogger logger, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<T> handler) where T : IStructuredLogLevel
    {
        var (template, arguments) = handler.GetTemplateAndArguments();
        logger.Log(Enum.Parse<LogLevel>(typeof(T).Name), exception, template, arguments);
    }

    internal static void Log<T>(this ILogger logger, EventId eventId, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<T> handler) where T : IStructuredLogLevel
    {
        var (template, arguments) = handler.GetTemplateAndArguments();
        logger.Log(Enum.Parse<LogLevel>(typeof(T).Name), eventId, template, arguments);
    }

    internal static void Log<T>(this ILogger logger, EventId eventId, Exception exception, [InterpolatedStringHandlerArgument("logger")] StructuredLoggingInterpolatedStringHandler<T> handler) where T : IStructuredLogLevel
    {
        var (template, arguments) = handler.GetTemplateAndArguments();
        logger.Log(Enum.Parse<LogLevel>(typeof(T).Name), eventId, exception, template, arguments);
    }
    #endregion
}

[InterpolatedStringHandler]
public readonly ref struct StructuredLoggingInterpolatedStringHandler<TStructuredLogLevel>
{
    private readonly StringBuilder _template = null!;
    private readonly List<object> _arguments = null!;
    private static readonly Regex _structuredVariableRegex = new(@"^@(?<variableName>[a-zA-Z_][a-zA-Z0-9_]*)$", RegexOptions.Compiled);
    private static readonly Regex _plainVariableRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    public StructuredLoggingInterpolatedStringHandler(int literalLength, int formattedCount, ILogger logger, out bool enabled)
    {
        enabled = logger.IsEnabled(Enum.Parse<LogLevel>(typeof(TStructuredLogLevel).Name));
        if (!enabled)
            return;

        _template = new(literalLength);
        _arguments = new(formattedCount);
    }

    public void AppendLiteral(string s)
    {
        _template.Append(s.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
    }

    public void AppendFormatted<T>(T value, [CallerArgumentExpression("value")] string expression = null)
    {
        if (expression is not null)
        {
            // Check for structured logging with variable name: {@variable}
            var structuredMatch = _structuredVariableRegex.Match(expression);
            if (structuredMatch.Success)
            {
                var variableName = structuredMatch.Groups["variableName"].Value;
                _arguments.Add(value);
                _template.Append($"{{{variableName}}}");
                return;
            }

            // Check for plain variable: {variable}
            if (_plainVariableRegex.IsMatch(expression))
            {
                // Treat as plain string interpolation
                _template.Append(value);
                return;
            }
        }

        // Fallback: Append the value directly for literals
        _template.Append(value);
    }

    public (string, object[]) GetTemplateAndArguments()
    {
        return (_template?.ToString(), _arguments?.ToArray());
    }
}

