# Design: PR Reviewer

## Problem

Manual code review catches things reliably but doesn't scale with review
volume, and some categories of issue (SQL injection via string
concatenation, missing null checks, leftover debug statements) are
mechanical enough to check automatically before a human reviewer spends
time on them.

This tool takes a code diff, sends it to an AI model with a description
of what to look for, and returns a structured review. It's a personal
project, built partly as practice and partly as a working demo of the
design ideas below.

The plan is deliberately incremental: start with the smallest useful
version, keep the architecture honest from day one, and grow it toward
something that could realistically be shared with a team or published —
not a one-off script that gets rewritten later to do that.

## Goals (v1)

- Take a local diff file (e.g. `git diff > changes.diff`) and produce a
  structured `CodeReview` (summary, a list of findings, an overall
  verdict), printed to the console in a readable form.
- Let the review criteria be customized via an external guidelines file,
  without a rebuild.
- Keep reviews short and human-sounding: a few plain, one- or
  two-sentence comments that read like a teammate wrote them, not an
  AI-generated report. Enforced in three layers (prompt, output schema,
  and post-processing in code) — see *Key decisions*.
- Abstract the AI provider behind an `ICodeReviewer` interface, with
  three implementations — Claude, OpenAI, and Gemini — selectable via
  config, wired through a real DI container
  (`Microsoft.Extensions.DependencyInjection`) in a composition root —
  not just a design placeholder, an actual working example of
  constructor injection and interface-based substitution in a plain
  console app.
- Make the model configurable per provider, defaulting to the cheapest
  (or free-tier) option, so cost is a deliberate choice rather than a
  hardcoded constant.
- Default to a safe, zero-cost `dry_run` mode so a fresh checkout can
  never make a real (billed) API call by accident.
- Structure the code so later milestones (GitHub integration, a local
  LLM provider, config management, packaging) are additions, not
  rewrites.

## Deferred to later milestones (not "never")

These are out of scope for v1 specifically because they're the next
things on the roadmap below, not because they're out of scope for the
project as a whole:

- Automatic posting of the review anywhere — v1 is console-only output.
- Posting the review back to GitHub as a PR comment. Reading a PR by
  URL is already done (pulled forward from v2, see *Architecture*).
- A local LLM provider (e.g. via Ollama) — `ICodeReviewer` already
  supports it with zero changes to calling code, which is the point of
  the abstraction, but it isn't implemented in v1. Real testing right
  now is centered on Gemini's free tier; a local server is a genuinely
  different failure profile (model availability, weaker
  structured-output reliability) worth tackling as its own milestone
  rather than folding in untested.
- Rate limiting and cost controls beyond the model default, `dry_run`
  and `max_total_content_chars` — need to exist before this is used
  against real traffic, deferred only because v1 is single-diff,
  on-demand, manually invoked. (A basic retry on temporary API errors
  was pulled forward from v3: Gemini's free tier returns 503 "high
  demand" often enough to make the tool frustrating without it.)
- Packaging/distribution (e.g. as a `dotnet tool`) and versioning — real
  requirements for anything meant to be shared, deferred until the core
  functionality is solid.

## Architecture

Five responsibilities, kept separate so each can change independently:

1. **Where the diff comes from** — abstracted behind an `IDiffSource`
   interface, with two implementations. `LocalFileDiffSource` reads a
   diff from a local file. `GitHubPrDiffSource` fetches a pull request
   from the GitHub REST API given its URL
   (`https://github.com/owner/repo/pull/123`, also accepting the
   `/files` tab and other forms copied from a browser). Asking the API
   for the `application/vnd.github.diff` media type returns the whole PR
   as one unified diff, so the rest of the pipeline can't tell the two
   sources apart. The composition root picks the source from the
   command-line argument: anything starting with `http://` or
   `https://` is a URL (and a URL that isn't a GitHub PR is rejected
   with a clear message rather than treated as a file name).

   Public repos need no token (GitHub allows 60 unauthenticated requests
   an hour). An optional `github_token` / `GITHUB_TOKEN` covers private
   repos and higher limits. Not-found, rate-limit and bad-token
   responses each get their own one-line message.

2. **What the review should look for, and how it should read** — an
   optional `review_guidelines.md` file, loaded at run time and combined
   with the diff into the final prompt by `IPromptBuilder`. It holds
   both the review criteria and the writing style (short, plain, no
   preamble, with example comments showing the tone). Editable without
   recompiling, so the review criteria can be tuned by anyone using the
   tool. `IPromptBuilder` is injected into every `ICodeReviewer`
   implementation via its constructor, so the "diff + guidelines → one
   prompt string" logic is written once and shared, not duplicated per
   provider. Before the diff goes into the prompt, `DiffLineNumberer`
   prefixes every hunk line with its line number in the new file, and
   the prompt tells the model to copy those numbers. Models asked to
   count lines from the `@@` headers themselves were off by one or two;
   with the numbers written out, findings land on the right line. If the
   file is missing, a short built-in instruction is used instead (with a
   note on stderr), so a review still works and still aims for the short
   style.

   Both `config.json` and `review_guidelines.md` are read from the
   app's own folder (`AppContext.BaseDirectory`), not the current
   directory, so the tool can be run from inside any repo; the build
   copies both files there. Reading from the current directory meant
   that running it from another repo silently fell back to defaults,
   i.e. a dry run.

   Before any reviewer runs, a diff longer than `max_total_content_chars`
   is refused with a message rather than sent — a basic guard against an
   accidentally huge, costly request. `text_extensions` and
   `max_file_size_chars` (per-file filtering) are in the config template
   but not applied yet; they belong in the diff source and come in a
   later step.

3. **Which AI provider generates the review** — abstracted behind an
   `ICodeReviewer` interface:

   ```csharp
   public interface ICodeReviewer
   {
       Task<CodeReview> ReviewAsync(string diff);
   }
   ```

   Three v1 implementations: `ClaudeCodeReviewer` (Anthropic Messages
   API), `OpenAiCodeReviewer` (OpenAI Chat Completions/Responses API),
   and `GeminiCodeReviewer` (Gemini API). Each one builds its prompt via
   the injected `IPromptBuilder`, calls its own API using its own
   provider's native structured-output/JSON mode, and parses the result
   into a `CodeReview`.

   `GeminiCodeReviewer` is the first one built. It calls
   `models/{model}:generateContent` with `responseMimeType:
   application/json` and a `responseSchema`, and logs the input/output
   token counts from each response so the cost of every real review is
   visible. Gemini also offers a newer Interactions endpoint;
   `generateContent` was chosen because it's the long-standing, fully
   supported one, and switching later would only touch this class. API
   errors (bad key, unknown model, quota) surface as a one-line message
   using the API's own error text.

   A fourth implementation, `DryRunCodeReviewer`, makes no API call at
   all and returns a fixed placeholder review. It's what `dry_run = true`
   selects (see *Key decisions*).

   ```csharp
   public record CodeReview(
       string Summary,
       IReadOnlyList<ReviewFinding> Findings,
       ReviewVerdict Verdict
   );

   public record ReviewFinding(string File, int? Line, Severity Severity, string Message);

   public enum Severity { Info, Suggestion, Warning, Critical }
   public enum ReviewVerdict { Approve, ApproveWithComments, RequestChanges }
   ```

   The JSON schema each reviewer sends with its request carries a
   `description` on every field — e.g. `summary`: "one sentence",
   `message`: "one or two short sentences, plain text, no markdown" — so
   the length and style rules travel with the schema itself, not only in
   the prompt. Field descriptions are used rather than hard schema limits
   (`maxLength` and similar), since support for those varies between
   providers and a too-long answer should be trimmed, not rejected.

   If a provider's response doesn't parse as valid structured output,
   the reviewer falls back to a single `Warning`-severity finding
   carrying the raw text, rather than throwing — a malformed response
   degrades the review, it doesn't crash the tool. Parsing and this
   fallback live in one shared `ReviewResponseParser`, since every
   provider is asked for the same shape. An answer the provider blocked
   (e.g. a safety block) takes the same path, with the block reason as
   the text. (This fallback path
   is the main reason a local LLM provider is deferred rather than
   shipped untested — smaller local models are the likeliest to hit it.)

4. **What actually gets shown** — the `CodeReview` coming back from a
   provider is not printed as-is. A `ReviewFilter` step applies rules in
   code that the model can't talk its way around: drop findings below
   `min_severity`, keep at most `max_findings` (most severe first), and
   recompute the verdict from the findings that remain so the verdict
   and the visible findings always agree. A console printer then renders
   the result in a fixed format decided by this tool, never by the
   model: one block per finding, then the verdict. Each block has
   `file:line — status` (*Must fix* / *Should fix* / *Optional* /
   *FYI*, from the severity), a one-sentence message, the line of code
   the finding points at (`-`), and the suggested fix (`+`) when the
   model gave one. Both `min_severity` and `max_findings` live in
   `config.json`.

   The `-` line is looked up in the diff by the tool (via the same
   `DiffParser` that numbers the lines for the prompt), not copied by
   the model, so it's always quoted exactly. The fix comes from a
   `suggestion` field in the schema: the corrected line, an empty string
   for "delete this line", or null when there's no small, concrete fix,
   so the model isn't pushed into guessing large rewrites. A fix
   identical to the current line is dropped. The per-finding shape (one
   location, one problem, one proposed fix) is also exactly what posting
   findings as separate GitHub review comments with suggested changes
   would need, if that's added later.

5. **How the pieces get assembled** — a composition root in `Program.cs`
   using `Microsoft.Extensions.DependencyInjection`, the same DI
   container ASP.NET Core uses. If `dry_run` is true it registers
   `DryRunCodeReviewer`; otherwise it reads `config.json`'s `provider`
   field and registers the matching `ICodeReviewer` implementation
   (each constructed with `IPromptBuilder` injected in turn). A
   `ReviewRunner` receives the diff source, reviewer, filter and printer
   by constructor injection and runs them in order; nothing downstream
   of the registration needs to know or care which reviewer was chosen,
   dry run included.

### Data flow

```
diff (local file, or a GitHub PR URL)
        │
        ▼
IDiffSource.GetDiffAsync()
        │
        ▼
ICodeReviewer.ReviewAsync(diff)   (DI picked the implementation at startup)
   │
   ├─ dry_run = true → DryRunCodeReviewer
   │     fixed placeholder CodeReview, no API call, no cost
   │
   └─ dry_run = false → Claude / OpenAI / Gemini, based on config.provider
         ├─ IPromptBuilder.BuildPrompt(diff) → diff + review_guidelines.md
         ├─ call the provider's API
         └─ parse response into CodeReview (fallback to a raw-text
            finding if the response doesn't parse)
        │
        ▼
ReviewFilter
   (min_severity, max_findings, verdict recomputed from what's left)
        │
        ▼
CodeReview -> printed to console in a fixed compact format
              (later: posted back to GitHub)
```

The dry-run placeholder goes through the same filter and printer as a
real review, so the output path can be checked at zero cost. Its
findings cover every severity, so the effect of `min_severity` and
`max_findings` is visible in a dry run.

### Why this split

The review-generation logic shouldn't need to know or care whether the
diff came from a file on disk or a GitHub API call, or which provider
produced the review — that's exactly the kind of decision the
Open/Closed Principle argues for isolating behind an interface: new diff
sources and new AI providers get *added*, not built by modifying
existing, already-working code. The same reasoning applies to output:
printing to console today and posting to GitHub later should be two
implementations of one output abstraction, not a rewrite of the review
logic.

## Key decisions

**Configuration: `config.json` for settings, environment variable takes
precedence for secrets.** A single JSON file is a convenient home for
non-secret settings (`max_file_size_chars`, `text_extensions`, and so
on) and worth keeping. Putting secrets in the same file as plain text is
the common mistake: the file gets copied, shared, committed, or deployed
to a folder that ends up web-accessible, and the key goes with it.

The settled approach: a `config.json.template` (masked placeholder
values, committed to git — documents the shape of config for anyone
cloning the repo) and a real `config.json` (your actual key(s),
git-ignored, never committed). If `ANTHROPIC_API_KEY` / `OPENAI_API_KEY`
/ `GEMINI_API_KEY` is set as an environment variable, it takes precedence
over the matching value in `config.json`.

**AI provider abstracted behind `ICodeReviewer`, resolved through a real
DI container.** A simple `if/switch` picking which API to call would
work functionally, but using
`Microsoft.Extensions.DependencyInjection` for this specifically
demonstrates the same registration pattern used in ASP.NET Core's
`Program.cs` (`services.AddScoped<IThing, Implementation>()`), just in a
plain console app and with the implementation chosen at runtime from
config instead of being fixed at compile time. This is a deliberate v1
goal, not just an architectural nicety — the project is partly meant to
be a concrete, working example of constructor injection and
interface-based substitution. Three implementations rather than two
makes that point more convincingly: two providers can look like a
coincidence, three can't.

**Structured output (`CodeReview`) instead of raw text, with a
fallback.** An earlier version of this design deferred structured output
to a later milestone. Returning `Task<CodeReview>` from `ReviewAsync`
moves that into v1, because Claude, OpenAI, and Gemini all have
reasonably reliable native structured-output/JSON modes in practice, so
asking for `CodeReview` directly isn't significantly harder than asking
for prose. The one place this gets genuinely riskier is a smaller local
model, which is exactly why the local LLM provider stays deferred rather
than shipping alongside the three cloud providers.

**Guidelines threaded in via a shared `IPromptBuilder`, not a method
parameter.** `ReviewAsync(string diff)` only takes the diff — keeping
the interface minimal and provider-focused. Each `ICodeReviewer`
implementation gets `IPromptBuilder` injected via its constructor and
calls `BuildPrompt(diff)` internally to combine the diff with
`review_guidelines.md`. This keeps prompt-building logic in one place
instead of duplicated across three (soon four) providers, and is a
second, smaller example of the same DI pattern used for the providers
themselves.

**Model is configurable per provider, defaults to the cheapest (or
free) option.** Hardcoding a model constant means cost is an accident of
whatever was typed in at the time, not a decision. Reading `model` from
`config.json` makes cost a visible, deliberate setting, with the option
to switch to a more capable (pricier) model once review quality matters
more than iteration speed. Kept as a free-text string rather than an
enum, since model IDs change over time and shouldn't require a code
change to update.

**`dry_run` is a config setting, defaulting to `true` — not a CLI flag.**
Defaulting `dry_run` to `true` means a fresh checkout, or a config file
you haven't looked at in a while, can never make a real billed API call
by accident — the failure mode of "forgot dry_run was on" is
self-revealing (you keep getting placeholder text instead of a real
review), which is safer than a flag you might forget to pass. Turning
real reviews on is the deliberate action, not turning them off. The
same applies when `config.json` is missing or has no `dry_run` entry:
it counts as `true`.

**Dry run is an `ICodeReviewer` implementation, not a branch in the
pipeline.** An earlier version of this design checked `dry_run` before
calling the reviewer. Making it `DryRunCodeReviewer`, chosen by the
composition root like any provider, means the pipeline code has no
dry-run special case at all, the filter and printer get exercised by
every dry run, and the interface-swapping the project is meant to
demonstrate works before any API key exists.

**Interface-first for the diff source, the AI provider, and eventually
output.** Slight extra structure up front, in exchange for later
milestones — a new diff source, a new provider, a new output target —
being additive rather than requiring a rewrite of working code.

**Guidelines as an external file, not hardcoded prompt text.** Keeps the
"what to review for" concern editable by anyone using the tool, without
touching source code.

**Concise, human-sounding reviews, enforced in three layers.** AI models
left to their defaults write long, hedged, report-style reviews ("Great
work! One thing I noticed…"), which people learn to skim past. The goal
is the opposite: a few short comments that read like a teammate left
them. No single layer can guarantee that, so there are three:

1. *Prompt* — `review_guidelines.md` states the style rules and includes
   example comments. Examples steer tone better than descriptions do.
   Cheapest to change, but a request, not a guarantee.
2. *Schema* — field descriptions in each provider's structured-output
   schema repeat the length rules at the point where the model fills in
   each field. Still a request, but a harder one to drift from.
3. *Code* — `ReviewFilter` and the console printer. The only layer that
   is guaranteed: severity cut-off, a cap on the number of findings, a
   verdict that matches what's shown, and a fixed layout. This is also
   what keeps smaller or wordier models (including the future local
   provider) in line.

Layers 1 and 2 shape what the model writes; layer 3 decides what the
reader actually sees.

## Security considerations

- `config.json` (the real one, with your key(s)) must never be
  committed — enforced via `.gitignore`. Only `config.json.template`,
  with masked placeholder values, is tracked in git.
- Diff content is sent to a third-party API (Anthropic, OpenAI, or
  Google, depending on provider) for analysis. As with any tool that
  sends code to an external service, this shouldn't be run against diffs
  containing credentials, private keys, or sensitive personal data.
  Free tiers add a second concern: their terms may allow the provider
  to keep and use submitted content, so confidential code belongs on a
  paid tier only. Both are called out in the README ("What gets sent
  where").
- All provider keys follow the same pattern in `config.json.template`:
  masked placeholder, real value only ever in the git-ignored
  `config.json` or an environment variable. Only the key matching the
  active `provider` needs to be real; the unused ones can stay as
  placeholders.
- The optional GitHub token follows the same pattern (masked
  placeholder in the template, `GITHUB_TOKEN` takes precedence). It
  only needs read access, so a fine-grained token limited to read-only
  "Pull requests" is enough; posting reviews back later will need write
  access, which is a reason to keep that feature separate and opt-in.
- Before this is used against a real team's PRs: rate limiting and a
  cost ceiling on API usage, since an unbounded loop or a misbehaving
  caller could run up real API costs. `dry_run` defaulting to `true` and
  a cheap (or free-tier) default model address dev-time cost;
  production-scale cost control is still a v3+ concern.

## Roadmap

**v1 — current milestone.** Local diff file in, structured `CodeReview`
printed to console. Single implementation of `IDiffSource`.
`ICodeReviewer` abstraction with Claude, OpenAI, and Gemini
implementations (each backed by a shared `IPromptBuilder`), resolved via
a DI container based on `config.provider`. Config via `config.json`
(from `config.json.template`) with env-var override, a configurable
model per provider (default: cheapest or free-tier), and `dry_run`
defaulting to `true`. Concise reviews via `review_guidelines.md`, schema
field descriptions, and a `ReviewFilter` (`min_severity`,
`max_findings`) in front of a compact console printer. Manually invoked.

**v2 — GitHub integration and a local LLM provider.**
`GitHubPrDiffSource` fetches a diff directly from a PR URL, with the
entry point detecting local path vs. PR URL — *done early*. Still to
come: the tool can post the review back as a PR comment. `LocalCodeReviewer` adds a fourth
`ICodeReviewer` implementation, pointed at a local server (e.g. Ollama)
— same interface, no API key, exercising the fallback-on-unparsable-output
path built in v1.

**v3 — hardening.** Retry/backoff on transient API failures (a basic
version — `TransientRetryHandler`, 3 retries on 429/5xx — is already
in. It waits as long as the server asks, from a `Retry-After` header
or, for Google APIs, the `retryDelay` in the error body, up to 60 s;
a longer requested wait, such as a daily quota, stops at once rather
than spending more requests against the same limit. Without a hint it
waits 2, 5, then 10 s), sensible
timeouts, structured error messages instead of raw exceptions,
production-scale rate limiting and cost ceilings. Automated tests are
already in, pulled forward from here — see *Testing*.

## Testing

An xUnit project in `tests/PrReviewer.Tests`, run with `dotnet test`
from the repo root (the root `PR_Reviewer.slnx` holds both projects).
The solution uses the XML `.slnx` format, the .NET 10 default: every
tool that can build a `net10.0` project reads it, so the classic `.sln`
would add no compatibility. There must be only one solution file in the
root — with two, a bare `dotnet build` stops with MSB1011 ("more than
one project or solution file").
The app project sits in the repo root, so it explicitly excludes
`tests/**`; otherwise the SDK would compile the test code into the app.

Tests never touch the network, a real API key, or the real
`config.json`:

- Web calls go through a `FakeHttpHandler` that returns canned
  responses and records each request, so the Gemini request shape, the
  GitHub headers, and every retry path are checked without a server.
- `ConfigLoader.Load` and `PromptBuilder.LoadGuidelines` take an
  optional folder (and the config loader an environment-variable
  lookup), defaulting to the app's folder and the real environment.
  Tests pass a temporary folder and a fake environment. This matters
  because the build copies the real `config.json` into the test output
  folder too.
- `ReviewRunner` is tested end to end with a fixed diff and a reviewer
  returning a canned answer, checking the exact console output.

A quick mutation check when the suite was added (breaking the verdict
rule, and shifting line numbers by one) failed 1 and 11 tests
respectively, so the tests do catch real regressions. `DiffParser.Parse`
is now well covered, which makes the SonarQube complexity finding on it
(S3776) safe to address by splitting the method, if wanted.

**v4 — shareable.** Packaged for easy distribution (e.g. `dotnet tool
install`), a proper configuration story beyond a single env var (a
config file with a documented, git-ignored template; or a secret-store
integration), versioned releases, and a contributor-facing README section
if this ends up open to other people's changes.

Each milestone is meant to build on working code from the previous one,
not replace it — that's the point of the `IDiffSource` and
`ICodeReviewer` abstractions and the deferred-not-cut framing above.
