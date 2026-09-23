using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrReviewer.Configuration;
using PrReviewer.Domain;
using PrReviewer.Prompts;

namespace PrReviewer.Reviewers;

/// <summary>
/// Reviews a diff with Google's Gemini API (generateContent), using its
/// native JSON mode with a response schema so the answer comes back in
/// the CodeReview shape.
/// </summary>
public class GeminiCodeReviewer(
    HttpClient http,
    IPromptBuilder promptBuilder,
    AppConfig config,
    TextWriter log) : ICodeReviewer
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    public async Task<CodeReview> ReviewAsync(string diff)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = promptBuilder.BuildPrompt(diff) } }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = ResponseSchema()
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{BaseUrl}{Uri.EscapeDataString(config.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", config.GeminiApiKey);
        request.Content = JsonContent.Create(body);

        // A review takes anywhere from a few seconds to a minute; without
        // this line the tool looks frozen while it waits.
        log.WriteLine($"Reviewing with Gemini {config.Model}...");
        using var response = await http.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(
                $"Gemini returned {(int)response.StatusCode} {response.ReasonPhrase}: {ErrorMessage(responseText)}");
        }

        using var document = JsonDocument.Parse(responseText);
        LogUsage(document.RootElement);
        return ReviewResponseParser.Parse(AnswerText(document.RootElement));
    }

    /// <summary>
    /// The CodeReview shape in Gemini's schema format. The descriptions
    /// repeat the length and style rules from review_guidelines.md right
    /// where the model fills in each field.
    /// </summary>
    private static JsonObject ResponseSchema() => new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["summary"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "One sentence about the change as a whole. Plain text."
            },
            ["findings"] = new JsonObject
            {
                ["type"] = "ARRAY",
                ["description"] = "Real problems only. An empty list is a fine answer.",
                ["items"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["file"] = new JsonObject
                        {
                            ["type"] = "STRING",
                            ["description"] = "File path as shown in the diff."
                        },
                        ["line"] = new JsonObject
                        {
                            ["type"] = "INTEGER",
                            ["nullable"] = true,
                            ["description"] = "The line number shown before the \"|\" on the diff "
                                              + "line, or null if the finding is about the file "
                                              + "as a whole."
                        },
                        ["severity"] = EnumSchema<Severity>(),
                        ["message"] = new JsonObject
                        {
                            ["type"] = "STRING",
                            ["description"] = "One short sentence, plain text, no markdown: what is "
                                              + "wrong and why it matters. Put the fix in suggestion, "
                                              + "not here."
                        },
                        ["suggestion"] = new JsonObject
                        {
                            ["type"] = "STRING",
                            ["nullable"] = true,
                            ["description"] = "The corrected code for that line, exactly as it should "
                                              + "read, with no line number, \"|\" or +/- prefix. Use an "
                                              + "empty string if the fix is to delete the line. Use null "
                                              + "if the fix isn't a small, concrete change to this line "
                                              + "(a few lines at most)."
                        }
                    },
                    ["required"] = new JsonArray { "file", "line", "severity", "message", "suggestion" }
                }
            },
            ["verdict"] = EnumSchema<ReviewVerdict>()
        },
        ["required"] = new JsonArray { "summary", "findings", "verdict" }
    };

    private static JsonObject EnumSchema<TEnum>() where TEnum : struct, Enum => new()
    {
        ["type"] = "STRING",
        ["enum"] = new JsonArray(Enum.GetNames<TEnum>().Select(n => (JsonNode)n).ToArray())
    };

    /// <summary>
    /// candidates[0].content.parts[*].text, or a note on why there is no
    /// answer (e.g. a safety block), which then shows up as the fallback
    /// finding.
    /// </summary>
    private static string AnswerText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            var reason = root.TryGetProperty("promptFeedback", out var feedback)
                         && feedback.TryGetProperty("blockReason", out var block)
                ? block.GetString()
                : "unknown";
            return $"Gemini returned no answer (block reason: {reason}).";
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            var reason = candidate.TryGetProperty("finishReason", out var finish) ? finish.GetString() : "unknown";
            return $"Gemini returned no answer (finish reason: {reason}).";
        }

        return string.Concat(parts.EnumerateArray()
            .Where(p => p.TryGetProperty("text", out _))
            .Select(p => p.GetProperty("text").GetString()));
    }

    /// <summary>
    /// Token counts go to the log so the cost of each real review is
    /// visible.
    /// </summary>
    private void LogUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
        {
            return;
        }

        var input = usage.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
        var output = usage.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
        log.WriteLine($"Gemini {config.Model}: {input} input / {output} output tokens.");
    }

    private static string ErrorMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? responseText;
            }
        }
        catch (JsonException)
        {
            // Not JSON; show it as-is below.
        }

        return responseText;
    }
}
