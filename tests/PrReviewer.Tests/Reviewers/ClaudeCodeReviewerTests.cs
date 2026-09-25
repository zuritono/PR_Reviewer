using System.Net;
using System.Text.Json.Nodes;
using PrReviewer.Configuration;
using PrReviewer.Domain;
using PrReviewer.Prompts;
using PrReviewer.Reviewers;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests.Reviewers;

public class ClaudeCodeReviewerTests
{
    private static readonly AppConfig Config = new() { AnthropicApiKey = "test-key", Model = "claude-test" };

    /// <summary>A successful Messages API response carrying the given answer text.</summary>
    private static HttpResponseMessage Answer(string text, string stopReason = "end_turn") => FakeHttpHandler.Json(
        HttpStatusCode.OK,
        new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
            ["stop_reason"] = stopReason,
            ["usage"] = new JsonObject { ["input_tokens"] = 100, ["output_tokens"] = 20 }
        }.ToJsonString());

    private static (ClaudeCodeReviewer Reviewer, FakeHttpHandler Server, StringWriter Log) Create(
        params Func<HttpResponseMessage>[] responses)
    {
        var server = new FakeHttpHandler(responses);
        var log = new StringWriter();
        return (new ClaudeCodeReviewer(new HttpClient(server), new PromptBuilder("GUIDELINES"), Config, log),
            server, log);
    }

    [Fact]
    public async Task Calls_messages_with_the_key_and_api_version_headers()
    {
        var (reviewer, server, _) = Create(() => Answer("""{"summary":"s","findings":[],"verdict":"Approve"}"""));

        await reviewer.ReviewAsync("diff --git a/x b/x");

        var request = Assert.Single(server.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task Sends_the_prompt_and_asks_for_json_schema_in_the_review_shape()
    {
        var (reviewer, server, _) = Create(() => Answer("""{"summary":"s","findings":[],"verdict":"Approve"}"""));

        await reviewer.ReviewAsync("diff --git a/x b/x");

        var body = JsonNode.Parse(server.Bodies.Single())!;
        Assert.Equal("claude-test", body["model"]!.GetValue<string>());
        Assert.StartsWith("GUIDELINES", body["messages"]![0]!["content"]!.GetValue<string>());
        var schema = body["output_config"]!["format"]!;
        Assert.Equal("json_schema", schema["type"]!.GetValue<string>());
        Assert.False(schema["schema"]!["additionalProperties"]!.GetValue<bool>());
        var finding = schema["schema"]!["properties"]!["findings"]!["items"]!;
        Assert.Equal(["file", "line", "severity", "message", "suggestion"],
            finding["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("null", finding["properties"]!["suggestion"]!["anyOf"]![1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Returns_the_parsed_review_and_logs_token_usage()
    {
        var (reviewer, _, log) = Create(() => Answer(
            """{"summary":"s","findings":[{"file":"a.cs","line":3,"severity":"Warning","message":"m","suggestion":"x"}],"verdict":"RequestChanges"}"""));

        var review = await reviewer.ReviewAsync("diff");

        Assert.Equal(new ReviewFinding("a.cs", 3, Severity.Warning, "m", "x"), Assert.Single(review.Findings));
        Assert.Contains("Claude claude-test: 100 input / 20 output tokens.", log.ToString());
    }

    [Fact]
    public async Task Api_error_becomes_a_provider_exception_with_the_api_message()
    {
        var (reviewer, _, _) = Create(() => FakeHttpHandler.Json(HttpStatusCode.Unauthorized,
            """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}"""));

        var error = await Assert.ThrowsAsync<ProviderException>(() => reviewer.ReviewAsync("diff"));

        Assert.Equal("Claude returned 401 Unauthorized: invalid x-api-key", error.Message);
    }

    [Fact]
    public async Task Refusal_becomes_a_fallback_finding_with_the_reason()
    {
        var (reviewer, _, _) = Create(() => Answer("I can't review this.", "refusal"));

        var review = await reviewer.ReviewAsync("diff");

        Assert.Contains("Claude refused the request: I can't review this.", Assert.Single(review.Findings).Message);
    }
}
