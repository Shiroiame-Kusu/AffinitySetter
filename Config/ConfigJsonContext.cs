using System.Text.Json.Serialization;
using AffinitySetter.Utils;
using System.Collections.Generic;

namespace AffinitySetter.Config;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<AffinityRule>))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(List<CoreFrequencyLimit>))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}

