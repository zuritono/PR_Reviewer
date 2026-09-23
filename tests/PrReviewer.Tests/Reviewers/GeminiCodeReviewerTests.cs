using System.Net;
using System.Text.Json.Nodes;
using PrReviewer.Configuration;
using PrReviewer.Domain;
using PrReviewer.Prompts;
using PrReviewer.Reviewers;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests.Reviewers;

public class GeminiCodeReviewerTests
{
    private static readonly AppConfig Config = new() { GeminiApiKey = "test-key", Model = "gemini-test" };

    /// <summary>A successful Gemini response carrying the given answer text.</summary>
    private static HttpResponseMessage Answer(string text) => FakeHttpHandler.Json(HttpStatusCode.OK,
        new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = text } } } }
            },
            ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 100, ["candidatesTokenCount"] = 20 }
        }.ToJsonString());

    private static (GeminiCodeReviewer Reviewer, FakeHttpHandler Server, StringWriter Log) Create(params Func<HttpResponseMessage>[] responses)
    {
        var server = new FakeHttpHandler(responses);
        var log = new StringWriter();
        return (new GeminiCodeReviewer(new HttpClient(server), new PromptBuilder("GUIDELINES"), Config, log), server, log);
    }

    [Fact]
    public async Task Calls_generateContent_for_the_configured_model_with_the_key_header()
    {
        var (reviewer, server, _) = Create(() => Answer("""{"summary":"s","findings":[],"verdict":"Approve"}"""));

        await reviewer.ReviewAsync("diff --git a/x b/x");

        var request = Assert.Single(server.Requests);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent", request.RequestUri!.ToString());
        Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task Sends_the_prompt_and_asks_for_json_in_the_review_shape()
    {
        var (reviewer, server, _) = Create(() => Answer("""{"summary":"s","findings":[],"verdict":"Approve"}"""));

        await reviewer.ReviewAsync("diff --git a/x b/x");

        var body = JsonNode.Parse(server.Bodies.Single())!;
        Assert.StartsWith("GUIDELINES", body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>());
        var config = body["generationConfig"]!;
        Assert.Equal("application/json", config["responseMimeType"]!.GetValue<string>());
        var finding = config["responseSchema"]!["properties"]!["findings"]!["items"]!;
        Assert.Equal(["file", "line", "severity", "message", "suggestion"],
            finding["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(finding["properties"]!["suggestion"]!["nullable"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Returns_the_parsed_review_and_logs_token_usage()
    {
        var (reviewer, _, log) = Create(() => Answer(
            """{"summary":"s","findings":[{"file":"a.cs","line":3,"severity":"Warning","message":"m","suggestion":"x"}],"verdict":"RequestChanges"}"""));

        var review = await reviewer.ReviewAsync("diff");

        Assert.Equal(new ReviewFinding("a.cs", 3, Severity.Warning, "m", "x"), Assert.Single(review.Findings));
        Assert.Contains("Gemini gemini-test: 100 input / 20 output tokens.", log.ToString());
    }

    [Fact]
    public async Task Api_error_becomes_a_provider_exception_with_the_api_message()
    {
        var (reviewer, _, _) = Create(() => FakeHttpHandler.Json(HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid.","status":"INVALID_ARGUMENT"}}"""));

        var error = await Assert.ThrowsAsync<ProviderException>(() => reviewer.ReviewAsync("diff"));

        Assert.Equal("Gemini returned 400 Bad Request: API key not valid.", error.Message);
    }

    [Fact]
    public async Task Blocked_answer_becomes_a_fallback_finding_with_the_reason()
    {
        var (reviewer, _, _) = Create(() => FakeHttpHandler.Json(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}"""));

        var review = await reviewer.ReviewAsync("diff");

        Assert.Contains("block reason: SAFETY", Assert.Single(review.Findings).Message);
    }
}
