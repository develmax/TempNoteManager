using System.Text.RegularExpressions;

namespace TempNoteManager.Services;

public static partial class CategoryColorService
{
    private static readonly (string Color, string[] Keywords)[] KeywordColors =
    [
        ("#2563EB", ["api", "http", "endpoint", "service", "интеграц", "сервер", "backend", "бэкенд"]),
        ("#059669", ["done", "готов", "success", "успех", "финал", "clean", "чист"]),
        ("#0F766E", ["data", "csv", "json", "xml", "sql", "database", "данн", "таблиц", "база"]),
        ("#EA580C", ["todo", "fix", "bug", "ошиб", "правк", "сроч", "urgent", "важн"]),
        ("#DC2626", ["secret", "token", "key", "password", "auth", "безопас", "парол", "ключ"]),
        ("#7C3AED", ["idea", "draft", "concept", "design", "иде", "чернов", "план", "архитект"]),
        ("#0891B2", ["ui", "xaml", "frontend", "css", "view", "интерфейс", "экран", "окно"]),
        ("#9333EA", ["ai", "llm", "prompt", "model", "summary", "ии", "модель", "промпт"]),
        ("#BE123C", ["delete", "trash", "remove", "archive", "удал", "корзин", "архив"]),
        ("#4B5563", ["misc", "other", "разн", "проч", "unknown", "непонят"])
    ];

    private static readonly string[] FallbackPalette =
    [
        "#2563EB",
        "#0F766E",
        "#7C3AED",
        "#EA580C",
        "#0891B2",
        "#059669",
        "#BE123C",
        "#9333EA",
        "#4B5563",
        "#CA8A04"
    ];

    public static string SuggestColor(string name, string description)
    {
        var text = $"{name} {description}".Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return FallbackPalette[0];
        }

        var bestColor = string.Empty;
        var bestScore = 0;

        foreach (var candidate in KeywordColors)
        {
            var score = candidate.Keywords.Count(text.Contains);
            if (score > bestScore)
            {
                bestScore = score;
                bestColor = candidate.Color;
            }
        }

        if (!string.IsNullOrWhiteSpace(bestColor))
        {
            return bestColor;
        }

        var hash = 17;
        foreach (var character in text)
        {
            hash = unchecked(hash * 31 + character);
        }

        var index = Math.Abs(hash) % FallbackPalette.Length;
        return FallbackPalette[index];
    }

    public static bool TryNormalizeHex(string value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (ShortHexRegex().IsMatch(text))
        {
            normalized = $"#{text[1]}{text[1]}{text[2]}{text[2]}{text[3]}{text[3]}".ToUpperInvariant();
            return true;
        }

        if (FullHexRegex().IsMatch(text))
        {
            normalized = text.ToUpperInvariant();
            return true;
        }

        return false;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{3}$")]
    private static partial Regex ShortHexRegex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex FullHexRegex();
}
