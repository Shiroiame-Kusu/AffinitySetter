using System.Text.Json.Serialization;
using AffinitySetter.Utils;
using System.Collections.Generic;

namespace AffinitySetter.Config;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<AffinityRule>))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}

