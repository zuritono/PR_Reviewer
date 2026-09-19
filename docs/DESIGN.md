# Design: PR Reviewer

## Problem

Manual code review catches things reliably but doesn't scale with review
volume, and some categories of issue (SQL injection via string
concatenation, missing null checks, leftover debug statements) are
mechanical enough to check automatically before a human reviewer spends
time on them.

This tool takes a code diff, sends it to Claude with a description of
what to look for, and returns a structured review. It's a personal
project, built partly as practice and partly as a working demo of the
design ideas below.

The plan is deliberately incremental: start with the smallest useful
version, keep the architecture honest from day one, and grow it toward
something that could realistically be shared with a team or published —
not a one-off script that gets rewritten later to do that.

## Goals (v1)

- Take a local diff file (e.g. `git diff > changes.diff`) and produce a
  structured review, printed to the console.
- Let the review criteria be customized via an external guidelines file,
  without a rebuild.
- Make the model configurable, defaulting to the cheapest option, so
  cost is a deliberate choice rather than a hardcoded constant.
- Default to a safe, zero-cost `dry_run` mode so a fresh checkout can
  never make a real (billed) API call by accident.
- Structure the code so later milestones (GitHub integration, config
  management, packaging) are additions, not rewrites.

## Deferred to later milestones (not "never")

These are out of scope for v1 specifically because they're the next
things on the roadmap below, not because they're out of scope for the
project as a whole:

- Automatic posting of the review anywhere — v1 is console-only output.
- GitHub integration — the extension point exists (`IDiffSource`), but
  no implementation ships yet.
- Structured/parsed output (e.g. JSON) — v1 output is human-readable
  text; machine-readable output is a real future requirement once
  something else needs to consume the review programmatically.
- Retry/backoff on API failures, rate limiting, and cost controls beyond
  the model default and `dry_run` — need to exist before this is used
  against real traffic, deferred only because v1 is single-diff,
  on-demand, manually invoked.
- Packaging/distribution (e.g. as a `dotnet tool`) and versioning — real
  requirements for anything meant to be shared, deferred until the core
  functionality is solid.

## Architecture

Three responsibilities, kept separate so each can change independently:

1. **Where the diff comes from** — abstracted behind an `IDiffSource`
   interface. v1 ships one implementation, `LocalFileDiffSource`, which
   reads a diff from a local file. A future `GitHubPrDiffSource` would
   fetch a diff directly from the GitHub REST API given a PR URL.

2. **What the review should look for** — an optional
   `review_guidelines.md` file, loaded at run time and appended to the
   prompt sent to Claude. Editable without recompiling, so the review
   criteria can be tuned by anyone using the tool.

3. **How the review is generated** — a single component that builds the
   prompt (diff + guidelines) and calls the Claude API, independent of
   where the diff came from or how the result will be used. Takes the
   model as a parameter (from config) rather than a hardcoded constant,
   and checks `dry_run` before making any network call at all.

### Data flow

```
diff (local file today, GitHub PR later)
        │
        ▼
IDiffSource.GetDiffAsync()
        │
        ▼
prompt = diff + review_guidelines.md (if present)
        │
        ▼
config.dry_run == true? ──yes──▶ fixed placeholder review (no API call, no cost)
        │no
        ▼
Claude API (Messages endpoint, model from config)
        │
        ▼
review text -> printed to console (later: posted back, or returned as JSON)
```

### Why this split

The review-generation logic shouldn't need to know or care whether the
diff came from a file on disk or a GitHub API call — that's exactly the
kind of decision the Open/Closed Principle argues for isolating behind an
interface: new diff sources get *added*, not built by modifying existing,
already-working code. The same reasoning applies to output: printing to
console today and posting to GitHub later should be two implementations
of one output abstraction, not a rewrite of the review logic.

## Key decisions

**Configuration: `config.json` for settings, environment variable takes
precedence for the secret.** A single JSON file is a convenient home for
non-secret settings (`max_file_size_chars`, `text_extensions`, and so
on) and worth keeping. Putting secrets in the same file as plain text is
the common mistake: the file gets copied, shared, committed, or deployed
to a folder that ends up web-accessible, and the key goes with it.

The settled approach: a `config.json.template` (masked placeholder
values, committed to git — documents the shape of config for anyone
cloning the repo) and a real `config.json` (your actual key, git-ignored,
never committed). If `ANTHROPIC_API_KEY` is set as an environment
variable, it takes precedence over the value in `config.json` — this
keeps a convenient local-dev story (a file, filled in once) while still
supporting an env-var-only deployment later (CI, a container, or anyone
who doesn't want a key on disk at all) without a second code path.

**Model is configurable, defaults to the cheapest option.** Hardcoding a
model constant (as the original single-file example did) means cost is
an accident of whatever was typed in at the time, not a decision. Reading
`model` from `config.json` (default `claude-haiku-4-5`) makes cost a
visible, deliberate setting, with the option to switch to a more capable
(pricier) model once review quality matters more than iteration speed.

**`dry_run` is a config setting, defaulting to `true` — not a CLI flag.**
Originally considered as a CLI flag on the reasoning that a persistent
config setting could be left on and forgotten. Settled on config instead,
for the opposite reason: defaulting `dry_run` to `true` means a fresh
checkout, or a config file you haven't looked at in a while, can *never*
make a real billed API call by accident — the failure mode of "forgot
dry_run was on" is self-revealing (you keep getting placeholder text
instead of a real review), which is a safer default than "forgot to add
--dry-run and got charged without noticing." Turning real reviews on is
now the deliberate action, not turning them off.

**Interface-first for the diff source and, eventually, for output.**
Slight extra structure up front, in exchange for later milestones being
additive rather than requiring a rewrite of working code.

**Guidelines as an external file, not hardcoded prompt text.** Keeps the
"what to review for" concern editable by anyone using the tool, without
touching source code.

## Security considerations

- `config.json` (the real one, with your key) must never be committed —
  enforced via `.gitignore`. Only `config.json.template`, with masked
  placeholder values, is tracked in git.
- Diff content is sent to Anthropic's API for analysis. As with any tool
  that sends code to an external service, this shouldn't be run against
  diffs containing credentials, private keys, or sensitive personal data
  — worth calling out explicitly in the README once this is shared with
  anyone else.
- If a GitHub PAT is added later (for `GitHubPrDiffSource` /
  posting-back), it follows the same pattern: a field in
  `config.json.template` with a masked placeholder, the real value only
  ever in the git-ignored `config.json` or an environment variable.
- Before this is used against a real team's PRs: rate limiting and a
  cost ceiling on API usage, since an unbounded loop or a misbehaving
  caller could run up real API costs. `dry_run` defaulting to `true` and
  a cheap default model address dev-time cost; production-scale cost
  control is still a v3+ concern.

## Roadmap

**v1 — current milestone.** Local diff file in, review printed to
console. Single implementation of `IDiffSource`. Config via
`config.json` (from `config.json.template`) with env-var override, a
configurable model (default: cheapest), and `dry_run` defaulting to
`true` for zero-cost-by-default testing. Manually invoked.

**v2 — GitHub integration.** `GitHubPrDiffSource` fetches a diff
directly from a PR URL. The tool can post the review back as a PR
comment. Entry point detects local path vs. PR URL and picks the source.

**v3 — hardening.** Retry/backoff on transient API failures, sensible
timeouts, structured error messages instead of raw exceptions, basic
automated tests around the prompt-building and diff-source logic,
production-scale rate limiting and cost ceilings.

**v4 — shareable.** Packaged for easy distribution (e.g. `dotnet tool
install`), a proper configuration story beyond a single env var (a
config file with a documented, git-ignored template; or a secret-store
integration), versioned releases, and a contributor-facing README section
if this ends up open to other people's changes.

Each milestone is meant to build on working code from the previous one,
not replace it — that's the point of the `IDiffSource` abstraction and
the deferred-not-cut framing above.
