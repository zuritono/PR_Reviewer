# PR Reviewer

A console tool that reviews a code diff using an AI model and returns a
structured review. Starts small and simple, built to grow into a fully
functional tool suitable for sharing or handing off to others.

See [`docs/DESIGN.md`](docs/DESIGN.md) for the problem statement,
architecture, and the roadmap from the current simple version toward a
production-ready one.

## Status

Design complete. Implementation not yet started — see `docs/DESIGN.md`
for the v1 scope and the roadmap beyond it.

## Setup (once implemented)

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
bill by accident before you've even looked at the config.

```powershell
dotnet run -- sample-diff.txt
# prints a placeholder review — no API call, no cost, dry_run is still true
```

When you're ready to test against the real API, open `config.json` and
set `"dry_run": false`. If you forget this step, the output stays
obviously placeholder text rather than failing silently or costing
anything — you'll notice.

## Usage (once implemented)

```powershell
git diff > my-changes.diff
dotnet run -- my-changes.diff
```

A sample diff will be included so you can try it without a real change on
hand.

## Customizing the review

`review_guidelines.md`, once added, will be loaded automatically and
appended to every review prompt — edit it freely, no rebuild required.

Non-secret settings (provider, model, dry-run mode, max diff size, which
file extensions to include) live in `config.json` alongside the key —
see `config.json.template` for the full set.

## Project structure

See `docs/DESIGN.md` for the full architecture. In short: an
`IDiffSource` abstraction separates "where the diff comes from" from "how
it gets reviewed," and an `ICodeReviewer` abstraction separates "which AI
provider generates the review" from everything else — so a GitHub diff
source, or a different AI provider, can be added later without touching
unrelated code. Reviews come back as a structured `CodeReview` (summary,
findings, verdict), not raw text.
