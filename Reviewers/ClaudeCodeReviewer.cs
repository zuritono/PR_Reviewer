using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrReviewer.Configuration;
using PrReviewer.Domain;
using PrReviewer.Prompts;

namespace PrReviewer.Reviewers;

/// <summary>
/// Reviews a diff with Anthropic's Messages API, using native JSON
/// schema structured output so the answer comes back in the CodeReview
/// shape.
/// </summary>
public class ClaudeCodeReviewer(
    HttpClient http,
    IPromptBuilder promptBuilder,
    AppConfig config,
    TextWriter log) : ICodeReviewer
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    public async Task<CodeReview> ReviewAsync(string diff)
    {
        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["max_tokens"] = 8192,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = promptBuilder.BuildPrompt(diff)
                }
            },
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["schema"] = ResponseSchema()
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", config.AnthropicApiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        request.Content = JsonContent.Create(body);

        log.WriteLine($"Reviewing with Claude {config.Model}...");
        using var response = await http.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(
                $"Claude returned {(int)response.StatusCode} {response.ReasonPhrase}: {ErrorMessage(responseText)}");
        }

        using var document = JsonDocument.Parse(responseText);
        LogUsage(document.RootElement);
        return ReviewResponseParser.Parse(AnswerText(document.RootElement));
    }

    /// <summary>
    /// The CodeReview shape in JSON Schema. Anthropic requires
    /// additionalProperties: false on every object; field descriptions
    /// repeat the length and style rules where the model fills each field.
    /// </summary>
    private static JsonObject ResponseSchema()
    {
        var findings = Field(ArrayType, "Real problems only. An empty list is a fine answer.");
        findings["items"] = ObjectSchema(
            new JsonObject
            {
                ["file"] = Field(StringType, "File path as shown in the diff."),
                ["line"] = Field(IntegerType,
                    "The line number shown before the \"|\" on the diff line, or null if the finding "
                    + "is about the file as a whole.",
                    nullable: true),
                ["severity"] = EnumSchema<Severity>(),
                ["message"] = Field(StringType,
                    "One short sentence, plain text, no markdown: what is wrong and why it matters. "
                    + "Put the fix in suggestion, not here."),
                ["suggestion"] = Field(StringType,
                    "The corrected code for that line, exactly as it should read, with no line number, "
                    + "\"|\" or +/- prefix. Use an empty string if the fix is to delete the line. Use null "
                    + "if the fix isn't a small, concrete change to this line (a few lines at most).",
                    nullable: true)
            },
            "file", "line", "severity", "message", "suggestion");

        return ObjectSchema(
            new JsonObject
            {
                ["summary"] = Field(StringType, "One sentence about the change as a whole. Plain text."),
                ["findings"] = findings,
                ["verdict"] = EnumSchema<ReviewVerdict>()
            },
            "summary", "findings", "verdict");
    }

    private const string StringType = "string";
    private const string IntegerType = "integer";
    private const string ArrayType = "array";
    private const string ObjectType = "object";

    private static JsonObject ObjectSchema(JsonObject properties, params string[] required) => new()
    {
        ["type"] = ObjectType,
        ["properties"] = properties,
        ["required"] = new JsonArray(required.Select(n => (JsonNode)n).ToArray()),
        ["additionalProperties"] = false
    };

    private static JsonObject Field(string type, string description, bool nullable = false)
    {
        JsonObject field;
        if (nullable)
        {
            field = new JsonObject
            {
                ["anyOf"] = new JsonArray
                {
                    new JsonObject { ["type"] = type },
                    new JsonObject { ["type"] = "null" }
                }
            };
        }
        else
        {
            field = new JsonObject { ["type"] = type };
        }

        field["description"] = description;
        return field;
    }

    private static JsonObject EnumSchema<TEnum>() where TEnum : struct, Enum => new()
    {
        ["type"] = StringType,
        ["enum"] = new JsonArray(Enum.GetNames<TEnum>().Select(n => (JsonNode)n).ToArray())
    };

    /// <summary>
    /// Concatenated text content blocks, or a note on why there is no
    /// answer (e.g. a safety refusal), which then shows up as the
    /// fallback finding.
    /// </summary>
    private static string AnswerText(JsonElement root)
    {
        var stopReason = root.TryGetProperty("stop_reason", out var stop) ? stop.GetString() : null;
        if (stopReason is "refusal")
        {
            var refusal = TextBlocks(root);
            return string.IsNullOrWhiteSpace(refusal)
                ? "Claude returned no answer (stop reason: refusal)."
                : $"Claude refused the request: {refusal}";
        }

        var text = TextBlocks(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"Claude returned no answer (stop reason: {stopReason ?? "unknown"}).";
        }

        return text;
    }

    private static string TextBlocks(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Concat(content.EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text"
                            && block.TryGetProperty("text", out _))
            .Select(block => block.GetProperty("text").GetString()));
    }

    private void LogUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return;
        }

        var input = usage.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : 0;
        var output = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
        log.WriteLine($"Claude {config.Model}: {input} input / {output} output tokens.");
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
