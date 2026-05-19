using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TempNoteManager.Models;

namespace TempNoteManager.Services;

public sealed class AiSummaryService
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public async Task<string> SummarizeAsync(NoteFileItem item, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var sourceText = await GetSourceTextAsync(item, 9000, cancellationToken);
        var clippedText = sourceText.Length > 9000 ? sourceText[..9000] : sourceText;

        var request = new
        {
            model = settings.AiModel,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Ты помогаешь быстро ориентироваться в открытых файлах Notepad++. Дай краткое, точное описание на русском в 1-3 предложениях. Не выдумывай факты, которых нет в тексте."
                },
                new
                {
                    role = "user",
                    content = $"Файл: {item.DisplayName}\nПуть: {item.PathText}\n\nСодержимое:\n{clippedText}"
                }
            },
            temperature = 0.2,
            max_tokens = 180
        };

        return await SendChatAsync(request, settings, cancellationToken);
    }

    public async Task<IReadOnlyList<NoteTag>> ClassifyAsync(
        NoteFileItem item,
        IReadOnlyList<StorageCategory> categories,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (categories.Count == 0)
        {
            return [];
        }

        var sourceText = await GetSourceTextAsync(item, 7000, cancellationToken);
        var categoryText = string.Join(
            "\n",
            categories.Select(category => $"- {category.Name}: {category.Description}"));

        var request = new
        {
            model = settings.AiModel,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Ты классифицируешь файлы по пользовательским категориям. Верни только JSON вида {\"tags\":[{\"name\":\"Категория\",\"reason\":\"короткая причина\"}]}. Можно выбрать несколько категорий, если файл подходит. Если уверенности нет, верни пустой массив."
                },
                new
                {
                    role = "user",
                    content = $"Категории:\n{categoryText}\n\nФайл: {item.DisplayName}\nПуть: {item.PathText}\n\nСодержимое:\n{sourceText}"
                }
            },
            temperature = 0.1,
            max_tokens = 260
        };

        var responseText = await SendChatAsync(request, settings, cancellationToken);
        return ParseTags(responseText, categories);
    }

    public async Task<IReadOnlyList<SuggestedCategory>> SuggestMissingCategoriesAsync(
        IReadOnlyList<NoteFileItem> items,
        IReadOnlyList<StorageCategory> existingCategories,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var fileBlocks = items
            .Take(45)
            .Select(item =>
            {
                var text = !string.IsNullOrWhiteSpace(item.Summary) ? item.Summary : item.PreviewText;
                text = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
                if (text.Length > 900)
                {
                    text = text[..900];
                }

                return $"Файл: {item.DisplayName}\nПуть: {item.PathText}\nТекущие тэги: {item.TagsText}\nФрагмент:\n{text}";
            });

        var existing = existingCategories.Count == 0
            ? "Категорий пока нет."
            : string.Join("\n", existingCategories.Select(category => $"- {category.Name}: {category.Description}"));

        var request = new
        {
            model = settings.AiModel,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Ты помогаешь разобрать большой список файлов Notepad++. Предложи недостающие категории, которых нет в текущем списке. Верни только JSON вида {\"categories\":[{\"name\":\"...\",\"description\":\"...\",\"color\":\"#RRGGBB\",\"reason\":\"...\"}]}. Не дублируй существующие категории. Максимум 8 категорий."
                },
                new
                {
                    role = "user",
                    content = $"Существующие категории:\n{existing}\n\nФайлы:\n{string.Join("\n\n---\n\n", fileBlocks)}"
                }
            },
            temperature = 0.25,
            max_tokens = 900
        };

        var responseText = await SendChatAsync(request, settings, cancellationToken);
        return ParseSuggestedCategories(responseText, existingCategories);
    }

    private async Task<string> GetSourceTextAsync(NoteFileItem item, int maxCharacters, CancellationToken cancellationToken)
    {
        var sourceText = item.FullContent;
        if (string.IsNullOrWhiteSpace(sourceText) || sourceText == "Загрузка...")
        {
            sourceText = await FileTextReader.ReadPreviewAsync(item.ContentPath, maxCharacters, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            throw new InvalidOperationException("Нет текста для анализа.");
        }

        return sourceText.Length > maxCharacters ? sourceText[..maxCharacters] : sourceText;
    }

    private async Task<string> SendChatAsync(object request, AppSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.AiEnabled)
        {
            throw new InvalidOperationException("AI выключен в настройках.");
        }

        if (string.IsNullOrWhiteSpace(settings.AiEndpoint))
        {
            throw new InvalidOperationException("Не указан AI endpoint.");
        }

        if (string.IsNullOrWhiteSpace(settings.AiModel))
        {
            throw new InvalidOperationException("Не указана модель AI.");
        }

        var uri = new Uri(settings.AiEndpoint, UriKind.Absolute);
        var needsApiKey = !IsLocalEndpoint(uri);
        if (needsApiKey && string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            throw new InvalidOperationException("AI не подключен: укажите API key или переменную OPENAI_API_KEY.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AiApiKey);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = responseText.Length > 280 ? responseText[..280] + "..." : responseText;
            throw new InvalidOperationException($"AI вернул {(int)response.StatusCode}: {message}");
        }

        return ExtractSummary(responseText);
    }

    private static bool IsLocalEndpoint(Uri uri)
    {
        return uri.IsLoopback
               || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractSummary(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString()?.Trim() ?? string.Empty;
            }

            if (first.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString()?.Trim() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("AI ответил в неизвестном формате.");
    }

    private static IReadOnlyList<NoteTag> ParseTags(string responseText, IReadOnlyList<StorageCategory> categories)
    {
        var json = StripCodeFence(responseText);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tags", out var tagsElement)
                || tagsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<NoteTag>();
            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                var name = tagElement.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;

                var category = categories.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (category is null || result.Any(tag => tag.Name.Equals(category.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var reason = tagElement.TryGetProperty("reason", out var reasonElement)
                    ? reasonElement.GetString() ?? string.Empty
                    : string.Empty;

                result.Add(new NoteTag
                {
                    Name = category.Name,
                    Color = category.Color,
                    Reason = reason
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SuggestedCategory> ParseSuggestedCategories(
        string responseText,
        IReadOnlyList<StorageCategory> existingCategories)
    {
        var json = StripCodeFence(responseText);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("categories", out var categoriesElement)
                || categoriesElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<SuggestedCategory>();
            foreach (var categoryElement in categoriesElement.EnumerateArray())
            {
                var name = categoryElement.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(name)
                    || existingCategories.Any(category => category.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    || result.Any(category => category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var description = categoryElement.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;
                var reason = categoryElement.TryGetProperty("reason", out var reasonElement)
                    ? reasonElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;
                var color = categoryElement.TryGetProperty("color", out var colorElement)
                    ? colorElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

                if (!CategoryColorService.TryNormalizeHex(color, out var normalizedColor))
                {
                    normalizedColor = CategoryColorService.SuggestColor(name, description);
                }

                result.Add(new SuggestedCategory
                {
                    Name = name,
                    Description = description,
                    Color = normalizedColor,
                    Reason = reason
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || lastFence <= firstLineEnd)
        {
            return trimmed;
        }

        return trimmed[(firstLineEnd + 1)..lastFence].Trim();
    }
}
