using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Events;
using NanoBot.Services;
using OpenAI.Realtime;
using System.Text;
using System.Text.Json;

namespace NanoBot.Util;

internal static class ToolInvoker
{
    public static async Task<RealtimeFunctionCallOutputItem> InvokeFunctionAsync(
        string functionName,
        string functionCallId,
        string itemId,
        Dictionary<string, StringBuilder> functionArgumentBuildersById,
        IReadOnlyList<AIFunction> tools,
        ILogger logger,
        IEventBus bus,
        CancellationToken cancellationToken)
    {
        var argumentsString = functionArgumentBuildersById.GetValueOrDefault(itemId)?.ToString() ?? "{}";

        var function = tools.FirstOrDefault(t => t.Name == functionName)
            ?? throw new InvalidOperationException($"Function '{functionName}' not found in registered tools.");

        var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsString);
        var arguments = parsed != null ? new AIFunctionArguments(parsed) : null;

        try
        {
            bus.Publish<FunctionInvokingEvent>(null);
            logger.LogDebug("FunctionInvoking - {FunctionName} - Args: {Args}", functionName, argumentsString);

            var result = await function.InvokeAsync(arguments, cancellationToken);

            logger.LogDebug("FunctionInvoked - {FunctionName} - Result: {Result}", functionName, result);

            return RealtimeItem.CreateFunctionCallOutputItem(
                callId: functionCallId,
                functionOutput: ProcessFunctionResult(result));
        }
        finally
        {
            bus.Publish<FunctionInvokedEvent>(null);
        }
    }

    public static IEnumerable<RealtimeFunctionTool> ConvertToRealtimeTools(IReadOnlyList<AIFunction> tools)
    {
        foreach (var func in tools)
        {
            yield return func.AsOpenAIRealtimeFunctionTool();
        }
    }

    public static string ProcessFunctionResult(object functionResult)
    {
        if (functionResult is null) return string.Empty;
        return functionResult is string s ? s : JsonSerializer.Serialize(functionResult);
    }
}
