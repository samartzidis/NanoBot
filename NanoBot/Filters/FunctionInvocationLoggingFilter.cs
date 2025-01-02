using System.Text.Json;
using NanoBot.Events;
using NanoBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace NanoBot.Filters;

public class FunctionInvocationLoggingFilter : IFunctionInvocationFilter
{
    private readonly ILogger _logger;
    private readonly IEventBus _bus;

    public FunctionInvocationLoggingFilter(ILogger<FunctionInvocationLoggingFilter> logger, IEventBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        try
        {
            _bus.Publish<FunctionInvokingEvent>(this);

            var args = context?.Arguments == null ? string.Empty : JsonSerializer.Serialize(context.Arguments);
            _logger.LogDebug("FunctionInvoking - {PluginName}.{FunctionName} - Args: {Args}", context?.Function.PluginName, context?.Function.Name, args);

            await next(context);

            _logger.LogDebug("FunctionInvoked - {PluginName}.{FunctionName} - Result: {Result}", context?.Function.PluginName, context?.Function.Name, context?.Result);
        }
        finally
        {
            _bus.Publish<FunctionInvokedEvent>(this);
        }
    }
}