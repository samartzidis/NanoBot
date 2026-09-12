using System.ClientModel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace NanoBot.Plugins;

public sealed class PowerAIPlugin
{
    private static readonly SystemChatMessage SystemMessage = new(
        """
        You are a helpful assistant whose answers will be relayed verbally to a user by another AI.
        Provide clear, comprehensive answers. Include your reasoning and the final answer.
        Do not use markdown, tables, or formatting - respond in plain spoken language.
        """);

    private readonly ILogger _logger;
    private readonly ChatClient _chatClient;
    private readonly string _model;
    private readonly ChatCompletionOptions _chatOptions;

    public PowerAIPlugin(ILogger<PowerAIPlugin> logger, string apiKey, string model)
    {
        _logger = logger;
        _model = model;
        _chatClient = new ChatClient(_model, new ApiKeyCredential(apiKey));
        _chatOptions = new ChatCompletionOptions
        {
            ReasoningEffortLevel = ChatReasoningEffortLevel.None
        };

        _logger.LogInformation("PowerAI created with model '{Model}'", _model);
    }

    [Description("Send a question to a more powerful AI model and return its answer. Formulate the prompt as a self-contained question, including any relevant facts you've already gathered.")]
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

        var completion = await _chatClient.CompleteChatAsync(messages, _chatOptions, cancellationToken);
        var result = completion.Value;
        var response = result.Content.Count > 0 ? result.Content[0].Text : string.Empty;

        _logger.LogInformation("PowerAI response: {Response}", response);
        return response;
    }
}
