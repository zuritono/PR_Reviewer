# PR Reviewer

A console tool that reviews a code diff using an AI model and returns a
structured review. Starts small and simple, built to grow into a fully
functional tool suitable for sharing or handing off to others.

See [`docs/DESIGN.md`](docs/DESIGN.md) for the problem statement,
architecture, and the roadmap from the current simple version toward a
production-ready one.

## Status

Design complete. Implementation in progress: the pipeline runs end to
end (diff file → reviewer → filter → console), with a dry-run reviewer
and **Gemini** as the first real provider. Claude and OpenAI aren't
implemented yet and stop with a "not implemented yet" message — see
`docs/DESIGN.md` for the v1 scope and the roadmap beyond it.

## Setup

Only Gemini works for real reviews so far; the Claude and OpenAI steps
below apply once those providers are added.

1. Pick a provider: **Claude** (Anthropic), **OpenAI**, or **Gemini**
   (Google). Get an API key from whichever you choose:
   - Claude: https://console.anthropic.com/settings/keys
   - OpenAI: https://platform.openai.com/api-keys
   - Gemini: https://aistudio.google.com/apikey

   Claude and OpenAI offer a small amount of free credits on a new
   account, no card required to start. Gemini has an ongoing free tier
   for several models through Google AI Studio (free input/output
   tokens, rate-limited) — worth trying first if you want to test
   real reviews at zero cost; check
   https://ai.google.dev/gemini-api/docs/pricing for current details,
   since free-tier terms and included models change over time.

2. Configure the key using **one** of the following:

   **Option A — config file (recommended for local dev):**
   ```powershell
   Copy-Item config.json.template config.json
   ```
   Open `config.json`, set `"provider"` to `"claude"`, `"openai"`, or
   `"gemini"`, and fill in the matching key (`anthropic_api_key`,
   `openai_api_key`, or `gemini_api_key`). You only need the key for the
   provider you selected. `config.json` is git-ignored — it will never
   be committed.

   **Option B — environment variable:**
   ```powershell
   $env:ANTHROPIC_API_KEY = "sk-ant-..."
   # or, if using OpenAI:
   $env:OPENAI_API_KEY = "sk-..."
   # or, if using Gemini:
   $env:GEMINI_API_KEY = "..."
   ```
   Lasts for the current PowerShell window unless set persistently.
   Useful for CI, a container, or anywhere you'd rather not have a key on
   disk. The environment variable matching your selected provider takes
   precedence over `config.json`.

   Either way: `config.json.template` is the only config file meant to be
   committed — it holds masked placeholder values so anyone cloning the
   repo can see what's needed without any real secret being exposed.

3. Choose a model to match your provider. `config.json` defaults to
   `claude-haiku-4-5-20251001` (cheapest Claude option). If you set
   `"provider": "openai"`, change `"model"` to an OpenAI model such as
   `gpt-5.6-luna`; if you set `"provider": "gemini"`, use a Gemini model
   such as `gemini-3.5-flash-lite`. See the `_model_options` comment in
   `config.json.template` for current choices, or check
   https://docs.claude.com/en/docs/about-claude/models,
   https://developers.openai.com/api/docs/models, and
   https://ai.google.dev/gemini-api/docs/models directly — model IDs
   change over time.

   A fourth provider, a local LLM (e.g. via Ollama), is on the roadmap
   but not implemented yet — see `docs/DESIGN.md` for why it's deferred
   rather than shipped alongside these three.

## Testing without API costs

`config.json` includes `"dry_run": true` **by default**. While it's
true, the tool never calls any AI provider's API at all — it prints a
fixed placeholder review instead, so a fresh checkout can never rack up a
bill by accident before you've even looked at the config. A missing
`config.json`, or one without a `dry_run` entry, counts as `true` too.

```powershell
dotnet run -- samples/sample.diff
# prints a placeholder review — no API call, no cost, dry_run is still true
```

The placeholder has one finding of each severity, so you can see
`min_severity` and `max_findings` at work before going live.

When you're ready to test against the real API, open `config.json` and
set `"dry_run": false`. If you forget this step, the output stays
obviously placeholder text rather than failing silently or costing
anything — you'll notice.

## Usage

The intended workflow: in the repo you're working on, save the diff,
review it, read the result in the console, and decide what goes into
the PR. Nothing is posted anywhere.

```powershell
cd C:\path\to\your\work-repo
git diff main...HEAD > pr.diff   # what the PR will contain
prreview pr.diff
```

Other useful diffs: `git diff` (uncommitted changes), `git diff --cached`
(staged only).

`prreview` is a small function in your PowerShell profile
(`notepad $PROFILE`) that runs the tool from any folder:

```powershell
function prreview {
    dotnet run --project C:\Dev\PR_Reviewer\PR_Reviewer.csproj --no-launch-profile -- @args
}
```

`dotnet run` rebuilds first when anything changed, so it always uses
your latest code, `config.json` and `review_guidelines.md`. The tool
reads those two files from its own build folder, and every build copies
them there from the project folder — so edit them in the project
folder, never in `bin\`. `--no-launch-profile` stops Visual Studio's
launch profile (which points at the sample diff) from applying.

Without the function, run it from the project folder:

```powershell
cd C:\Dev\PR_Reviewer
dotnet run -- C:\path\to\pr.diff
```

`samples/sample.diff` is a small C# and SQL Server change with a few
deliberate problems (SQL built by concatenation, a leftover
`Console.WriteLine`, an undisposed reader, a stored procedure missing
`SET NOCOUNT ON` / `SET XACT_ABORT ON`), for trying the tool without a
real change on hand.

## Customizing the review

Reviews are meant to be short and read like a teammate wrote them — a
few plain one- or two-sentence comments, not an AI-style report. Two
places control that:

- **`review_guidelines.md`** — loaded automatically and added to every
  review prompt. It sets what to look for, what to skip, and how to
  write, with example comments showing the tone. Edit it freely; the
  next `prreview` or `dotnet run` picks up the change, no code changes
  needed.
- **`min_severity` and `max_findings` in `config.json`** — applied in
  code after the model responds, so they hold no matter what the model
  returns. Findings below `min_severity` are dropped (default:
  `Suggestion`, which hides `Info` remarks), at most `max_findings` are
  kept (default: 10, most severe first), and the verdict is recomputed
  from what's left.

The output format itself is fixed by the tool: a one-sentence summary,
one line per finding, then the verdict:

```
Adds a customer order lookup and an archiving procedure.

src/UserRepo.cs:42 — This builds the SQL by concatenating userId. Use a parameter instead.
src/UserRepo.cs:88 — Leftover Console.WriteLine.

Request changes.
```

Other non-secret settings (provider, model, dry-run mode, max diff size,
which file extensions to include) live in `config.json` alongside the
key — see `config.json.template` for the full set.

## Project structure

See `docs/DESIGN.md` for the full architecture. In short: an
`IDiffSource` abstraction separates "where the diff comes from" from "how
it gets reviewed," and an `ICodeReviewer` abstraction separates "which AI
provider generates the review" from everything else — so a GitHub diff
source, or a different AI provider, can be added later without touching
unrelated code. Reviews come back as a structured `CodeReview` (summary,
findings, verdict), not raw text.
