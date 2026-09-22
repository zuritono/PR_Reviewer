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
- GitHub integration — the extension point exists (`IDiffSource`), but
  no implementation ships yet.
- A local LLM provider (e.g. via Ollama) — `ICodeReviewer` already
  supports it with zero changes to calling code, which is the point of
  the abstraction, but it isn't implemented in v1. Real testing right
  now is centered on Gemini's free tier; a local server is a genuinely
  different failure profile (model availability, weaker
  structured-output reliability) worth tackling as its own milestone
  rather than folding in untested.
- Retry/backoff on API failures, rate limiting, and cost controls beyond
  the model default and `dry_run` — need to exist before this is used
  against real traffic, deferred only because v1 is single-diff,
  on-demand, manually invoked.
- Packaging/distribution (e.g. as a `dotnet tool`) and versioning — real
  requirements for anything meant to be shared, deferred until the core
  functionality is solid.

## Architecture

Five responsibilities, kept separate so each can change independently:

1. **Where the diff comes from** — abstracted behind an `IDiffSource`
   interface. v1 ships one implementation, `LocalFileDiffSource`, which
   reads a diff from a local file. A future `GitHubPrDiffSource` would
   fetch a diff directly from the GitHub REST API given a PR URL.

2. **What the review should look for, and how it should read** — an
   optional `review_guidelines.md` file, loaded at run time and combined
   with the diff into the final prompt by `IPromptBuilder`. It holds
   both the review criteria and the writing style (short, plain, no
   preamble, with example comments showing the tone). Editable without
   recompiling, so the review criteria can be tuned by anyone using the
   tool. `IPromptBuilder` is injected into every `ICodeReviewer`
   implementation via its constructor, so the "diff + guidelines → one
   prompt string" logic is written once and shared, not duplicated per
   provider.

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
   degrades the review, it doesn't crash the tool. (This fallback path
   is the main reason a local LLM provider is deferred rather than
   shipped untested — smaller local models are the likeliest to hit it.)

4. **What actually gets shown** — the `CodeReview` coming back from a
   provider is not printed as-is. A `ReviewFilter` step applies rules in
   code that the model can't talk its way around: drop findings below
   `min_severity`, keep at most `max_findings` (most severe first), and
   recompute the verdict from the findings that remain so the verdict
   and the visible findings always agree. A console printer then renders
   the result in a fixed, compact format — one line per finding
   (`file:line — message`), then the verdict — so the layout is decided
   by this tool, never by the model. Both `min_severity` and
   `max_findings` live in `config.json`.

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
diff (local file today, GitHub PR later)
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
  containing credentials, private keys, or sensitive personal data —
  worth calling out explicitly in the README once this is shared with
  anyone else.
- All provider keys follow the same pattern in `config.json.template`:
  masked placeholder, real value only ever in the git-ignored
  `config.json` or an environment variable. Only the key matching the
  active `provider` needs to be real; the unused ones can stay as
  placeholders.
- If a GitHub PAT is added later (for `GitHubPrDiffSource` /
  posting-back), it follows the same pattern.
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
`GitHubPrDiffSource` fetches a diff directly from a PR URL. The tool can
post the review back as a PR comment. Entry point detects local path vs.
PR URL and picks the source. `LocalCodeReviewer` adds a fourth
`ICodeReviewer` implementation, pointed at a local server (e.g. Ollama)
— same interface, no API key, exercising the fallback-on-unparsable-output
path built in v1.

**v3 — hardening.** Retry/backoff on transient API failures, sensible
timeouts, structured error messages instead of raw exceptions, basic
automated tests around the prompt-building, diff-source,
review-generator, and `ReviewFilter` logic, production-scale rate limiting and cost
ceilings.

**v4 — shareable.** Packaged for easy distribution (e.g. `dotnet tool
install`), a proper configuration story beyond a single env var (a
config file with a documented, git-ignored template; or a secret-store
integration), versioned releases, and a contributor-facing README section
if this ends up open to other people's changes.

Each milestone is meant to build on working code from the previous one,
not replace it — that's the point of the `IDiffSource` and
`ICodeReviewer` abstractions and the deferred-not-cut framing above.
