using System.ClientModel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace NanoBot.Plugins.Native;

public sealed class PowerAiPlugin
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

    public PowerAiPlugin(ILogger<PowerAiPlugin> logger, string apiKey, string model)
    {
        _logger = logger;
        _model = model;
        _chatClient = new ChatClient(_model, new ApiKeyCredential(apiKey));
    }

    [Description("Send a question to a more powerful AI model and return its answer. Formulate the prompt as a self-contained question.")]
    public async Task<string> AskAsync(
        [Description("The question or prompt to send to the powerful AI model")] string prompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PowerAI [{Model}]: {Prompt}", _model, prompt);

        var completion = await _chatClient.CompleteChatAsync(
            [SystemMessage, new UserChatMessage(prompt)],
            cancellationToken: cancellationToken);

        var response = completion.Value.Content[0].Text;
        _logger.LogInformation("PowerAI response: {Response}", response);
        return response;
    }
}
