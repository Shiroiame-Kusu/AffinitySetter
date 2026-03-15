namespace AffinitySetter.Utils;

internal static class CpuSelectionParser
{
    public static int[] Parse(object? input)
    {
        if (input == null)
        {
            return Array.Empty<int>();
        }

        if (input is System.Text.Json.JsonElement element)
        {
            return ParseJsonElement(element);
        }

        if (input is IEnumerable<int> enumerable)
        {
            return enumerable.Distinct().OrderBy(x => x).ToArray();
        }

        if (input is string text)
        {
            return ParseString(text);
        }

        return Array.Empty<int>();
    }

    private static int[] ParseJsonElement(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => ParseString(element.GetString() ?? string.Empty),
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray()
                .Where(item => item.TryGetInt32(out _))
                .Select(item => item.GetInt32())
                .Distinct()
                .OrderBy(x => x)
                .ToArray(),
            _ => Array.Empty<int>()
        };
    }

    private static int[] ParseString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<int>();
        }

        var topology = CpuTopology.Instance;
        var resolved = topology.ResolveCpuExpression(input);
        if (resolved != null && resolved.Length > 0)
        {
            return resolved;
        }

        var cpus = new List<int>();
        foreach (var part in input.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var range = trimmed.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end) && start <= end)
                {
                    for (int cpu = start; cpu <= end; cpu++)
                    {
                        cpus.Add(cpu);
                    }
                }
            }
            else if (int.TryParse(trimmed, out int cpu))
            {
                cpus.Add(cpu);
            }
        }

        return cpus.Distinct().OrderBy(x => x).ToArray();
    }
}