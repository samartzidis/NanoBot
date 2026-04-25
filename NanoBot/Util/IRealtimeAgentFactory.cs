using NanoBot.Configuration;

namespace NanoBot.Util;

public interface IRealtimeAgentFactory
{
    RealtimeAgent Create(AgentConfig agentConfig);
}
