using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace NanoBot.Util;

public interface IDynamicOptions<out T> : IOptions<T> where T : class, new()
{
}

public class DynamicOptions<T> : IDynamicOptions<T> where T : class, new()
{
    private readonly IConfiguration _configuration;
    private readonly string _sectionName;

    public DynamicOptions(IConfiguration configuration, string sectionName = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _sectionName = sectionName;
    }

    // IOptions<T> implementation
    public T Value => Create(_sectionName);

    // Dynamically create and bind options from configuration
    public T Create(string name)
    {
        var options = new T();
        var section = string.IsNullOrEmpty(name)
            ? _configuration // Bind at root
            : _configuration.GetSection(name);

        section.Bind(options);
        return options;
    }
}