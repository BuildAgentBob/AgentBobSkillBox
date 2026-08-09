# UiPath VB.Netcode

Selector-free web automation for UiPath: browser network captures are turned into **Invoke Code** VB.NET that replays HTTP (cookies, tokens, HTML forms, XHR). No UI selectors.

## For AI agents

| Resource | Who reads it | Purpose |
|----------|--------------|---------|
| [`agent-bob-skills.md`](agent-bob-skills.md) | AI agents | Instructions for converting capture JSON → UiPath Invoke Code VB.NET |
| [`.cursor/skills/agent-bob-skills/SKILL.md`](.cursor/skills/agent-bob-skills/SKILL.md) | Cursor agents | Same skill, auto-discoverable |
| `AxxessAutomation/`, `SandataAutomation/` | AI agents | **Examples** of finished Invoke Code — match style, patterns, and argument naming |

When writing new VB.NET for UiPath:

1. Follow **agent-bob-skills** (no `Imports`, fully qualified types, parameterize dynamics, scope to the user’s ask).
2. **Read existing `.vb` files in this repo** for how code was written here (request helpers, cookie reuse, form/HTML parsing, error handling, Out args).
3. Prefer the closest example folder (JSON/API vs HTML/WebForms) and adapt to the **current** site/capture—not copy vendor-specific URLs blindly.

Humans: paste generated code into UiPath **Invoke Code** and wire In/Out arguments.

## Repo layout

| Path | Contents |
|------|----------|
| [`AxxessAutomation/`](AxxessAutomation/) | Okta → Identity → HomeCare session; claim/billing Invoke Code samples |
| [`SandataAutomation/`](SandataAutomation/) | Sandata login + schedule/WebForms + submission Invoke Code samples |
| [`agent-bob-skills.md`](agent-bob-skills.md) | Agent skill (source of truth for how to generate code) |
| [`.cursor/skills/agent-bob-skills/`](.cursor/skills/agent-bob-skills/) | Cursor copy of the skill |

## How the pipeline works

1. Capture browser traffic (`captured-api-workflow.json` or similar) while doing login or an action.
2. Agent reads **agent-bob-skills** + **existing VB samples in the repo**.
3. Agent emits Invoke Code VB scoped to the request (auth and/or post-login action).
4. Robot runs steps sharing `CookieContainer` (and any tokens/headers the capture shows).

Traffic may be HTML pages, form posts, XHR JSON/HTML, or OAuth redirects—not only REST APIs.

## Invoke Code conventions (all samples)

- No `Imports` / `Module` / `Class` / wrapping `Sub`
- Fully qualified types (`System.Net.HttpWebRequest`, `Newtonsoft.Json…`)
- UiPath variables as arguments by name
- `Try` / `Catch` → `errorMessage`
- Dynamics extracted at runtime or passed In—never hardcoded from captures

## Typical arguments

| Argument | Direction | Role |
|----------|-----------|------|
| `cookies` | In / InOut | Shared `System.Net.CookieContainer` |
| `errorMessage` | Out | Failure text |
| Credentials / business fields | In | username, ids, dates, … |
| Tokens / session bags | In or Out | As required by the capture |
| Response outs | Out | HTML, JSON, DataTable, flags |

## Example sample chains

**Axxess:** `OktaAxxessAuth` → `FinalizeAxxessAuth` → claim steps (`GenerateClaim`, `CreateClaim`, …).

**Sandata:** `SandataLogin` → schedule/submission steps (`GetSchedule`, `PrepareSubmissionIntent`, …).

## Requirements

- UiPath with VB.NET Invoke Code
- Newtonsoft.Json available to the process
- Network access from the robot to target hosts
