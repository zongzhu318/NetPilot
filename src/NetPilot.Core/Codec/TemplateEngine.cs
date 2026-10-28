using System.Globalization;
using System.Text.RegularExpressions;

namespace NetPilot.Core.Codec;

public sealed partial class TemplateEngine
{
    private readonly object _sync = new();
    private long _sequence;

    public string Expand(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return VariableRegex().Replace(template, match =>
        {
            var name = match.Groups["name"].Value.ToLowerInvariant();
            var argument = match.Groups["arg"].Success ? match.Groups["arg"].Value : null;
            return name switch
            {
                "time" => DateTime.Now.ToString(
                    string.IsNullOrWhiteSpace(argument) ? "yyyy-MM-dd HH:mm:ss.fff" : argument,
                    CultureInfo.InvariantCulture),
                "random" => NextRandom(argument),
                "seq" => NextSequence(argument),
                _ => match.Value
            };
        });
    }

    public void Reset() => Interlocked.Exchange(ref _sequence, 0);

    private string NextRandom(string? argument)
    {
        int min = 0;
        int max = 999_999;
        if (!string.IsNullOrWhiteSpace(argument))
        {
            var parts = argument.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out min);
                int.TryParse(parts[1], out max);
            }
            else if (int.TryParse(parts[0], out var maxOnly))
            {
                max = maxOnly;
            }
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        return Random.Shared.Next(min, max + 1).ToString(CultureInfo.InvariantCulture);
    }

    private string NextSequence(string? argument)
    {
        lock (_sync)
        {
            if (long.TryParse(argument, out var start) && _sequence == 0)
            {
                _sequence = start;
            }

            _sequence++;
            return _sequence.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static string HelpText =>
        "支持变量：${time} / ${time:yyyy-MM-dd HH:mm:ss} / ${random} / ${random:100-999} / ${seq} / ${seq:1000}";

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z]+)(?::(?<arg>[^}]*))?\}")]
    private static partial Regex VariableRegex();
}
