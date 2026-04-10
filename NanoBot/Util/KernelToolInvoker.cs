using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Realtime;
using System.Text;
using System.Text.Json;

namespace NanoBot.Util;

/// <summary>
/// Stateless utility for bridging Semantic Kernel functions with the OpenAI Realtime API.
/// Converts kernel plugins to conversation tools and invokes function calls.
/// </summary>
internal static class KernelToolInvoker
{
    private const string FunctionNameSeparator = "-";

    /// <summary>
    /// Invokes a function call from the Realtime API and returns the output item.
    /// </summary>
    public static async Task<RealtimeItem> InvokeFunctionAsync(
        string functionName,
        string functionCallId,
        string itemId,
        Dictionary<string, StringBuilder> functionArgumentBuildersById,
        Kernel kernel,
        CancellationToken cancellationToken)
    {
        var (parsedFunctionName, pluginName) = ParseFunctionName(functionName);
        var argumentsString = functionArgumentBuildersById.GetValueOrDefault(itemId)?.ToString() ?? "{}";
        var arguments = DeserializeArguments(argumentsString);

        var functionCallContent = new FunctionCallContent(
            functionName: parsedFunctionName,
            pluginName: pluginName,
            id: functionCallId,
            arguments: arguments);

        var resultContent = await functionCallContent.InvokeAsync(kernel, cancellationToken);

        return RealtimeItem.CreateFunctionCallOutput(
            callId: functionCallId,
            output: ProcessFunctionResult(resultContent.Result));
    }

    /// <summary>
    /// Converts Semantic Kernel plugins to OpenAI Realtime conversation tools.
    /// </summary>
    public static IEnumerable<ConversationFunctionTool> ConvertKernelFunctions(Kernel kernel)
    {
        foreach (var plugin in kernel.Plugins)
        {
            foreach (var metadata in plugin.GetFunctionsMetadata())
            {
                var toolDef = metadata.ToOpenAIFunction().ToFunctionDefinition(false);

                yield return new ConversationFunctionTool(name: toolDef.FunctionName)
                {
                    Description = toolDef.FunctionDescription,
                    Parameters = toolDef.FunctionParameters
                };
            }
        }
    }

    /// <summary>
    /// Parses a fully qualified function name into its component parts.
    /// Format: "PluginName-FunctionName" or just "FunctionName".
    /// </summary>
    public static (string FunctionName, string PluginName) ParseFunctionName(string fullyQualifiedName)
    {
        string pluginName = null;
        string functionName = fullyQualifiedName;

        int separatorPos = fullyQualifiedName.LastIndexOf(FunctionNameSeparator, StringComparison.Ordinal);
        if (separatorPos >= 0)
        {
            pluginName = fullyQualifiedName.AsSpan(0, separatorPos).Trim().ToString();
            functionName = fullyQualifiedName.AsSpan(separatorPos + FunctionNameSeparator.Length).Trim().ToString();
        }

        return (functionName, pluginName);
    }

    /// <summary>
    /// Deserializes JSON arguments string to KernelArguments.
    /// </summary>
    public static KernelArguments DeserializeArguments(string argumentsString)
    {
        var arguments = JsonSerializer.Deserialize<KernelArguments>(argumentsString);

        if (arguments is not null)
        {
            var names = arguments.Names.ToArray();
            foreach (var name in names)
                arguments[name] = arguments[name]?.ToString();
        }

        return arguments;
    }

    /// <summary>
    /// Processes a function result into a string suitable for the Realtime API.
    /// </summary>
    public static string ProcessFunctionResult(object functionResult)
    {
        return functionResult is string s ? s : JsonSerializer.Serialize(functionResult);
    }
}
