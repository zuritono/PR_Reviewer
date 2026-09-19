# PR Reviewer

A console tool that reviews a code diff using Claude and returns a
structured review. Starts small and simple, built to grow into a fully
functional tool suitable for sharing or handing off to others.

See [`docs/DESIGN.md`](docs/DESIGN.md) for the problem statement,
architecture, and the roadmap from the current simple version toward a
production-ready one.

## Status

Design complete. Implementation not yet started — see `docs/DESIGN.md`
for the v1 scope and the roadmap beyond it.

## Setup (once implemented)

1. Get an Anthropic API key from https://console.anthropic.com/settings/keys
   (the API console — a separate credential from a claude.ai chat login).
   New accounts receive a small amount of free credits automatically, no
   card required to start.

2. Configure the key using **one** of the following:

   **Option A — config file (recommended for local dev):**
   ```powershell
   Copy-Item config.json.template config.json
   ```
   Then open `config.json` and replace the placeholder with your real key.
   `config.json` is git-ignored — it will never be committed.

   **Option B — environment variable:**
   ```powershell
   $env:ANTHROPIC_API_KEY = "sk-ant-..."
   ```
   This only lasts for the current PowerShell window unless set as a
   persistent Windows user environment variable. Useful for CI, a
   container, or anywhere you'd rather not have a key on disk at all.

   If both are present, the **environment variable takes precedence**
   over `config.json`.

   Either way: `config.json.template` is the only config file meant to be
   committed — it holds masked placeholder values so anyone cloning the
   repo can see what's needed without any real secret being exposed.

3. (Optional) Choose a model. `config.json` defaults to `claude-haiku-4-5`
   — the cheapest option, good for verifying the tool works before
   spending more on review quality. Change the `model` field to a more
   capable model (e.g. a current Sonnet model — check
   https://docs.claude.com/en/docs/about-claude/models for current IDs
   and pricing) once you're ready to rely on the actual reviews.

## Testing without API costs

`config.json` includes `"dry_run": true` **by default**. While it's
true, the tool never calls the Claude API at all — it prints a fixed
placeholder review instead, so a fresh checkout can never rack up a bill
by accident before you've even looked at the config.

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

Non-secret settings (model, dry-run mode, max diff size, which file
extensions to include) live in `config.json` alongside the key — see
`config.json.template` for the full set.

## Project structure

See `docs/DESIGN.md` for the full architecture. In short: an
`IDiffSource` abstraction separates "where the diff comes from" from "how
it gets reviewed," so a GitHub-backed source can be added later without
touching the review logic.
