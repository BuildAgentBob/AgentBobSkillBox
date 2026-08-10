---
name: agent-bob-skills
description: >-
  Converts BobScout / Agent Bob network captures (captured-api-workflow JSON)
  into UiPath Invoke Code VB.NET for selector-free HTTP automation of any web
  app. Use when the user attaches capture JSON from BobScout.DesktopApp, asks
  for Invoke Code, HttpWebRequest/HttpClient VB, cookie/session replay,
  form/XHR/HTML automation, or reverse-engineering a marked action from traffic.
---

# Agent Bob — Capture → UiPath Invoke Code (any web app)

## Goal

Turn BobScout network traffic into **UiPath Invoke Code** VB.NET that replays
the **minimum reliable HTTP workflow** for the **marked action** in the JSON.
No UiPath selectors / UI automation. Works for **any website**.

## Source of truth

1. **Attached capture JSON** — primary source. Discover the action from the file.
2. **This skill** — coding and workflow rules.
3. **`Samples/`** — style reference only (local or GitHub).

Do **not** invent the workflow from casual chat wording when the JSON already
marks an action (`actions[].name`, `description`, `order`, linked requests).

| Samples location | Path |
|------------------|------|
| Local | `Samples/` |
| GitHub | https://github.com/BuildAgentBob/AgentBobSkillBox/tree/main/Samples |

Examples: `Samples/AxxessAutomation/` (JSON/APIs/cookies/tokens),
`Samples/SandataAutomation/` (HTML/WebForms/form posts). Adapt to the current
site — do not copy hostnames, IDs, or secrets from samples.

## Capture source

Recorded with **[BobScout Desktop](https://github.com/BuildAgentBob/BobScout.DesktopApp)**.

```text
exportedAt
session { tabUrl, requestCount, actionCount }
actions[]
  id, name, description, order, requests[]
    url, method, type, tabUrl, timestamp
    requestHeaders[{ name, value }]
    requestBody { type, value }          # may be missing
    statusCode, responseHeaders, responseBody, mimeType
    authIndicators
```

## Discover the marked action first

Before coding, identify:

1. Marked action name/description in `actions[]`
2. What that action is meant to accomplish
3. Which requests **directly** perform it
4. Which requests are prerequisites only
5. Irrelevant browser/background traffic
6. Exact request order and dependencies
7. Dynamic vs static values
8. Auth/session mechanism (cookies, Bearer, CSRF, etc.)
9. Whether auth is the action itself or only a prerequisite
10. UiPath inputs required / useful outputs to return

**Authentication is not always the main action.** It may only unlock the marked
business step. Automate what the JSON marked — not a generic “full login product.”

## Filter noise — minimum chain only

Ignore unless required for tokens/HTML parsing:

- CSS, JS bundles, fonts, images, icons
- Analytics / telemetry / prefetch
- Unrelated APIs, duplicates, background polling

Reproduce the **smallest reliable** request sequence that performs the marked action.

Meaningful traffic includes HTML documents, form posts, XHR JSON/HTML, redirects
(with `#fragment` tokens), and multipart — not only REST JSON.

## Modes

- **Auth marked** — login/SSO → return session useful to later steps
- **Business action marked** — replay that action; take `in_Session` / cookies if needed
- **Mixed capture** — prefer separate Invoke Code units unless user wants one block

## Dynamic values (never hardcode)

Extract from earlier responses or expose as `in_…` args:

| Kind | Examples |
|------|----------|
| Session | cookies, session ids |
| Antiforgery | VIEWSTATE, CSRF, nonce |
| Tokens | access/refresh, Bearer, API keys |
| OAuth | code, state, PKCE, returnUrl |
| Business ids | record, batch, user, job, document ids |
| Inputs | email, password, dates, filters |

Never paste live passwords/JWTs from the capture into source or error text.

### Discovery over hardcoding

- Parse HTML for hidden inputs / inline JSON
- Parse JSON for ids/tokens needed next
- Follow `Location` / `redirectUrl` with the same cookie jar
- Read `#code=` / `#access_token=` from URL/Location **before** GET (fragments are not sent to the server)
- SPA login with no URL in HTML → use challenge/token XHR from the capture or linked JS

## Authentication / session

Follow what the capture **actually** sends:

- Cookies / HttpOnly cookies → `System.Net.CookieContainer` (required persistence path)
- Bearer → only if requests use `Authorization: Bearer …`
- CSRF / custom headers → as captured
- Do **not** assume a JWT implies Bearer if the app only uses cookies

### CookieContainer vs Session (critical)

Keep these as **separate** UiPath arguments — do not nest the jar inside the dictionary.

| Argument | Type | Role |
|----------|------|------|
| `out_CookieContainer` / `io_CookieContainer` (or `cookies`) | `System.Net.CookieContainer` | Auth cookie jar. Login creates it; later actions take it In/InOut and attach it to every request. |
| `out_Session` / `in_Session` | `Dictionary(Of String, Object)` | Optional **non-cookie** extras only (`baseUrl`, `csrfToken`, `buildDateIdentifier`, business ids, flags, …). |

When the capture authenticates with cookies (including HttpOnly JWT cookies):

1. Login must Out a real `CookieContainer` that already holds those cookies.
2. Later Invoke Code must reuse that **same** container argument — do not extract
   `accessToken` / `refreshToken` strings into `out_Session` and rebuild cookies.
3. After each authenticated call, return the same container (Out or InOut) so
   Set-Cookie rotations stay in the jar.
4. Copy `in_Session` → `out_Session` only for extras; never put `CookieContainer`
   (or raw token cookie values) into the session dictionary.

See `Samples/Rockae/RockaeLogin.vb` and `Samples/SandataAutomation/` for the pattern.

## Headers

Reproduce only headers the server needs (`Accept`, `Content-Type`, `Origin`,
`Referer`, `Authorization`, CSRF, app-specific).

Do **not** copy browser chrome unless proven required:

`sec-ch-ua*`, `sec-fetch-*`, `priority`, pseudo-headers (`:authority`, …),
redundant `Content-Length` (let the client set it).

## UiPath Invoke Code — output contract

**Critical:** code runs inside **Invoke Code**.

- **No** `Imports`, `Option`, `Module`, `Class`, `Namespace`, wrapping `Sub`/`Function`
- Fully qualified types
- Prefer stack used in `Samples/` (`System.Net.HttpWebRequest` + `CookieContainer`).
  `System.Net.Http.HttpClient` + `HttpClientHandler` + `CookieContainer` is OK if
  consistent and easier for the flow — do not mix styles randomly
- Avoid `Return` mid-flow for control (use `If`/`Else`/`Throw`); final errors via Catch
- Do not log passwords or tokens

### Logging (required)

Add `Console.WriteLine(...)` at **important steps** so UiPath job logs show progress.
Agents often omit this — do not.

Log at least:

- Start of the marked action
- Each essential HTTP call (method + path/purpose), e.g. `"POST /api/auth/login..."`
- Successful extraction of a dynamic value (name only — not secret values)
- Success completion of the action
- On failure, rely on `out_errorMessage` (optional short Console line without secrets)

**Never** write to the console:

- passwords, access/refresh tokens, full cookie headers, Authorization values
- full response bodies that may contain PII/secrets

Good:

```vb
Console.WriteLine("Starting marked action: Login")
Console.WriteLine("POST /api/auth/login")
Console.WriteLine("Authentication cookies received.")
Console.WriteLine("Login completed successfully.")
```

Bad:

```vb
Console.WriteLine("token=" & accessToken)
Console.WriteLine(responseBody)
```

### Argument policy (strict — avoid fluff)

| Rule | Detail |
|------|--------|
| Naming | Prefer `in_…` / `out_…` |
| Errors | Always `out_errorMessage As String` — empty on success; set only on failure |
| Inputs | Only what the marked action needs (`in_Email`, `in_RecordId`, …) |
| Outputs | Only what UiPath needs next (`out_CookieContainer`, `out_Session` extras, `out_BatchNumber`, …) |
| Forbid by default | `out_StatusCode`, `out_LoginSuccess`, `out_CookieHeader`, `out_ResponseBody`, raw JSON dumps, profile “verification” calls not required by the marked action |

Evaluate HTTP status **inside** the code. On failure: `Throw New System.Exception("…")`,
then `Catch` → `out_errorMessage = ex.Message` (no secrets in the message).

```vb
Try
    ' minimum API workflow for the marked action
Catch ex As System.Exception
    out_errorMessage = ex.Message
End Try
```

## Workflow checklist

1. Read `Samples/` for style.
2. Identify marked action from JSON.
3. Build minimum request chain + dynamics list.
4. Choose args per strict policy.
5. Emit complete Invoke Code body.
6. Document args table.

## Deliverable format (always)

### 1. Captured Action
Name + description from JSON.

### 2. Required API Flow
Essential requests in order (method + path + purpose).

### 3. Ignored Traffic
What was dropped and why (categories).

### 4. Authentication / Session
Mechanism + what persists (`out_CookieContainer` / `io_CookieContainer` separately from `out_Session` extras).

### 5. Dynamic Values
What is extracted vs taken as `in_…`.

### 6. UiPath Arguments

| Direction | Argument | Type | Purpose |
|-----------|----------|------|---------|

### 7. Complete VB.NET Invoke Code
Full body, not fragments. Must include `Console.WriteLine` progress logs (no secrets).

### 8. Result
Success outs vs `out_errorMessage` on failure.

If the capture lacks data for a required step, **say what is missing** — do not invent it.
