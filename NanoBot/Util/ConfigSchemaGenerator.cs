using Newtonsoft.Json.Schema.Generation;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NanoBot.Util;

internal static class ConfigSchemaGenerator
{
    private static readonly ConcurrentDictionary<Type, JSchema> _schemaCache = new();

    public static JSchema GetSchema<T>()
    {
        if (_schemaCache.TryGetValue(typeof(T), out var schema))
            return schema;

        var schemaGen = new JSchemaGenerator
        {
            SchemaIdGenerationHandling = SchemaIdGenerationHandling.TypeName,
            DefaultRequired = Required.Default, // Only apply 'required' if explicitly marked with [Required]
            SchemaLocationHandling = SchemaLocationHandling.Inline,
            SchemaReferenceHandling = SchemaReferenceHandling.None
        };

        // Add default generation providers
        schemaGen.GenerationProviders.Add(new StringEnumGenerationProvider());

        schema = schemaGen.Generate(typeof(T));

        _schemaCache[typeof(T)] = schema;

        return schema;
    }
}