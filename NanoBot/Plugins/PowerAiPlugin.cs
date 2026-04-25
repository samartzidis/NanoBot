using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace NanoBot.Plugins;

public sealed class PowerAIPlugin
{
    private const int MaxIterations = 10;

    private static readonly SystemChatMessage SystemMessage = new(
        """
        You are a helpful assistant whose answers will be relayed verbally to a user by another AI.
        You have tools available - use them as needed to gather information and answer accurately.
        Provide clear, comprehensive answers. Include your reasoning and the final answer.
        Do not use markdown, tables, or formatting - respond in plain spoken language.
        """);

    private readonly ILogger _logger;
    private readonly ChatClient _chatClient;
    private readonly string _model;
    private readonly IReadOnlyList<Microsoft.Extensions.AI.AIFunction> _tools;
    private readonly ChatCompletionOptions _chatOptions;

    public PowerAIPlugin(ILogger<PowerAIPlugin> logger, string apiKey, string model, IReadOnlyList<Microsoft.Extensions.AI.AIFunction> tools)
    {
        _logger = logger;
        _model = model;
        _chatClient = new ChatClient(_model, new ApiKeyCredential(apiKey));
        _tools = tools;
        _chatOptions = BuildOptions();

        _logger.LogInformation("PowerAI created with model '{Model}' and {ToolCount} tools: {Tools}",
            _model, _tools.Count, string.Join(", ", _tools.Select(t => t.Name)));
    }

    [Description("Send a question to a more powerful AI model and return its answer. Formulate the prompt as a self-contained question.")]
    public async Task<string> AskAsync(
        [Description("The question or prompt to send to the powerful AI model")] string prompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PowerAI [{Model}]: {Prompt}", _model, prompt);

        var messages = new List<ChatMessage>
        {
            SystemMessage,
            new UserChatMessage(prompt)
        };

        for (int i = 0; i < MaxIterations; i++)
        {
            var completion = await _chatClient.CompleteChatAsync(messages, _chatOptions, cancellationToken);
            var result = completion.Value;

            if (result.FinishReason != ChatFinishReason.ToolCalls)
            {
                var response = result.Content.Count > 0 ? result.Content[0].Text : string.Empty;
                _logger.LogInformation("PowerAI response: {Response}", response);
                return response;
            }

            messages.Add(new AssistantChatMessage(result));

            foreach (var toolCall in result.ToolCalls)
            {
                _logger.LogInformation("PowerAI tool call: {Tool}({Args})", toolCall.FunctionName, toolCall.FunctionArguments);
                var toolResult = await InvokeToolAsync(toolCall.FunctionName, toolCall.FunctionArguments, cancellationToken);
                _logger.LogInformation("PowerAI tool result: {Result}", toolResult);
                messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
            }
        }

        _logger.LogWarning("PowerAI reached max iterations ({Max}) without a final answer", MaxIterations);
        return "I wasn't able to reach a final answer within the allowed number of steps.";
    }

    private async Task<string> InvokeToolAsync(string functionName, BinaryData arguments, CancellationToken cancellationToken)
    {
        var func = _tools.FirstOrDefault(t => t.Name == functionName);
        if (func is null)
            return $"Error: Unknown function '{functionName}'";

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(arguments.ToString());
            var result = await func.InvokeAsync(parsed != null ? new Microsoft.Extensions.AI.AIFunctionArguments(parsed) : null, cancellationToken);
            return result?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerAI tool {Tool} failed", functionName);
            return $"Error: {ex.Message}";
        }
    }

    private ChatCompletionOptions BuildOptions()
    {
        var options = new ChatCompletionOptions();

        foreach (var func in _tools)
            options.Tools.Add(func.AsOpenAIChatTool());

        if (options.Tools.Count > 0)
            options.ToolChoice = ChatToolChoice.CreateAutoChoice();

        return options;
    }
}
