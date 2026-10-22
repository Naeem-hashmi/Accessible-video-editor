using System.Globalization;
using System.Text.RegularExpressions;

namespace AccessibleVideoEditor.Core.Text;

public enum TimeStyle
{
    /// <summary>"1 minute 5.5 seconds".</summary>
    Words,

    /// <summary>"1:05.5".</summary>
    Clock,
}

/// <summary>Formats times for speech and parses times the user types.</summary>
public static partial class TimeFormat
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Format(double seconds, TimeStyle style) =>
        style == TimeStyle.Clock ? Clock(seconds) : Words(seconds);

    /// <summary>Spoken form rounded to a tenth of a second, e.g. "1 minute 5.5 seconds".</summary>
    public static string Words(double seconds)
    {
        var tenths = (long)Math.Round(Math.Max(0, seconds) * 10, MidpointRounding.AwayFromZero);
        var hours = tenths / 36000;
        var minutes = tenths / 600 % 60;
        var secTenths = tenths % 600;

        var parts = new List<string>(3);
        if (hours > 0)
        {
            parts.Add(Plural(hours, "hour"));
        }

        if (minutes > 0)
        {
            parts.Add(Plural(minutes, "minute"));
        }

        if (secTenths > 0 || parts.Count == 0)
        {
            var value = secTenths / 10.0;
            var text = value.ToString(secTenths % 10 == 0 ? "0" : "0.0", Invariant);
            parts.Add(text + (secTenths == 10 ? " second" : " seconds"));
        }

        return string.Join(" ", parts);
    }

    /// <summary>Clock form, e.g. "1:05.5" or "1:02:03.0".</summary>
    public static string Clock(double seconds)
    {
        var tenths = (long)Math.Round(Math.Max(0, seconds) * 10, MidpointRounding.AwayFromZero);
        var hours = tenths / 36000;
        var minutes = tenths / 600 % 60;
        var secs = tenths / 10 % 60;
        var tenth = tenths % 10;
        return hours > 0
            ? string.Format(Invariant, "{0}:{1:00}:{2:00}.{3}", hours, minutes, secs, tenth)
            : string.Format(Invariant, "{0}:{1:00}.{2}", minutes, secs, tenth);
    }

    /// <summary>A short duration for settings, e.g. "1.5 seconds".</summary>
    public static string Seconds(double seconds) => Words(seconds);

    /// <summary>
    /// Parses a typed time. Accepts "83.5", "1:23.5", "1:02:03", "1m 30s", "2h", and relative values
    /// such as "+5" or "-2.5" (added to <paramref name="current"/>). A comma works as the decimal mark.
    /// The result is never negative.
    /// </summary>
    public static bool TryParse(string? input, double current, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim().ToLowerInvariant();
        var sign = 0;
        if (text.StartsWith('+'))
        {
            sign = 1;
            text = text[1..].TrimStart();
        }
        else if (text.StartsWith('-'))
        {
            sign = -1;
            text = text[1..].TrimStart();
        }

        if (!TryParseAbsolute(text, out var value))
        {
            return false;
        }

        seconds = sign == 0 ? value : current + sign * value;
        seconds = Math.Max(0, seconds);
        return true;
    }

    private static bool TryParseAbsolute(string text, out double value)
    {
        value = 0;
        if (text.Length == 0)
        {
            return false;
        }

        if (text.Contains(':'))
        {
            var pieces = text.Split(':');
            if (pieces.Length > 3)
            {
                return false;
            }

            double total = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                var isLast = i == pieces.Length - 1;
                if (!TryNumber(pieces[i], allowFraction: isLast, out var part) || part < 0)
                {
                    return false;
                }

                if (i > 0 && part >= 60)
                {
                    return false;
                }

                total = total * 60 + part;
            }

            value = total;
            return true;
        }

        var unitMatch = UnitPattern().Match(text);
        if (unitMatch.Success && (unitMatch.Groups["h"].Success || unitMatch.Groups["m"].Success ||
                                  unitMatch.Groups["s"].Success))
        {
            double total = 0;
            if (unitMatch.Groups["h"].Success && TryNumber(unitMatch.Groups["h"].Value, true, out var h))
            {
                total += h * 3600;
            }

            if (unitMatch.Groups["m"].Success && TryNumber(unitMatch.Groups["m"].Value, true, out var m))
            {
                total += m * 60;
            }

            if (unitMatch.Groups["s"].Success && TryNumber(unitMatch.Groups["s"].Value, true, out var s))
            {
                total += s;
            }

            value = total;
            return true;
        }

        return TryNumber(text, allowFraction: true, out value) && value >= 0;
    }

    private static bool TryNumber(string text, bool allowFraction, out double value)
    {
        text = text.Trim().Replace(',', '.');
        value = 0;
        if (text.Length == 0)
        {
            return false;
        }

        if (!allowFraction && text.Contains('.'))
        {
            return false;
        }

        return double.TryParse(text, NumberStyles.AllowDecimalPoint, Invariant, out value);
    }

    private static string Plural(long value, string unit) =>
        value == 1 ? $"1 {unit}" : $"{value.ToString(Invariant)} {unit}s";

    [GeneratedRegex(
        @"^\s*(?:(?<h>\d+(?:[.,]\d+)?)\s*(?:h|hr|hrs|hour|hours))?\s*(?:(?<m>\d+(?:[.,]\d+)?)\s*(?:m|min|mins|minute|minutes))?\s*(?:(?<s>\d+(?:[.,]\d+)?)\s*(?:s|sec|secs|second|seconds))?\s*$")]
    private static partial Regex UnitPattern();
}
