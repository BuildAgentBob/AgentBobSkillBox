---
name: agent-bob-skills
description: >-
  Converts BobScout / Agent Bob network captures (captured-api-workflow JSON)
  into UiPath Invoke Code VB.NET for selector-free HTTP automation of any web
  app. Use when the user attaches capture JSON from BobScout.DesktopApp, asks
  for Invoke Code, HttpWebRequest VB, cookie/session replay, form/XHR/HTML
  automation, or reverse-engineering login or post-login actions from traffic.
---

# Agent Bob — Capture → UiPath Invoke Code (any web app)

## Goal

Turn real browser traffic into **UiPath Invoke Code** VB.NET that replays the
same HTTP flow with `System.Net.HttpWebRequest` and a shared
`System.Net.CookieContainer`. **No UiPath selectors / UI automation.**

Works for **any website** (not limited to Axxess, Sandata, or any one vendor).
Local folders like `AxxessAutomation/` and `SandataAutomation/` are optional
style references only.

## Capture source

Traffic is recorded with **[BobScout Desktop](https://github.com/BuildAgentBob/BobScout.DesktopApp)**
(Agent Bob Electron app — Playwright network capture, exportable workflow JSON).
Accept the same JSON shape if exported from a related BobScout / extension tool.

## Capture shape

Typical BobScout export:

```text
exportedAt
session { tabUrl, requestCount, actionCount }
actions[]
  requests[]
    url, method, type, tabUrl, timestamp
    requestHeaders[{ name, value }]
    requestBody { type: raw|json|..., value }   # may be missing
    statusCode, responseHeaders, responseBody, mimeType
    authIndicators
```

`type` / `mimeType` may be document, xhr, fetch, script, etc. **Do not require
clean REST JSON APIs.** Meaningful traffic includes:

| Kind | Examples | How to automate |
|------|----------|-----------------|
| HTML document | login.aspx, schedule pages | GET page → parse hidden fields / tokens → POST form |
| Form POST | `application/x-www-form-urlencoded` | Rebuild body; re-extract VIEWSTATE-style fields each run |
| XHR / fetch JSON | `/api/...`, SPA backends | JSON serialize body; parse JSON response |
| XHR HTML / text | partial HTML, grids | POST/GET; regex or parse HTML response |
| Redirects | 302 Location, OAuth fragments | Manual redirect loop; preserve `#...` fragments |
| Multipart | file uploads | Rebuild multipart boundary body when needed |

If the capture only shows an HTML page load then a form post, that **is** the API.

## Modes (ask determines scope)

- **Auth** — build login/SSO → Out `cookies` (+ any tokens/session bags later steps need)
- **Action** — already logged in → replay only the action; In whatever those calls use
- **Mixed** — prefer separate Invoke Code units unless user wants one block

Never assume every job starts with login.

## Workflow

1. **Scope to the user ask** — only requests that implement that action.
2. **Filter noise** — static `.js`/`.css`/images/fonts/maps/analytics, unless they contain tokens the flow needs.
3. **Keep causality** — order matters: GET page (tokens) → POST → XHR → redirect.
4. **Classify each kept call** — HTML / form / JSON / redirect / file; match `Content-Type` and `Accept` from the capture.
5. **Infer session needs from the request** (not from a fixed vendor list):
   - Cookie header → `cookies` In/InOut
   - `Authorization` → token In arg (name from header scheme)
   - Custom headers that look like build/version/tenant IDs → In args extracted earlier or passed in
6. **Parameterize all dynamics** — see below.
7. **Emit Invoke Code body** — see Output contract.
8. **Document args** — name, direction, producer step.

## Dynamic values (never hardcode)

Anything that changes per user, session, or run must be an **argument** or
**extracted at runtime** from a previous response in the same flow.

| Kind | Examples | Handling |
|------|----------|----------|
| Cookies / session | auth cookies, ASP.NET_SessionId | Shared `CookieContainer` |
| Page antiforgery | `__VIEWSTATE`, `__EVENTVALIDATION`, CSRF, nonce | GET page each run; regex/parse; never paste capture values |
| Tokens | Bearer, access_token, id_token, API keys in headers | Extract or In arg |
| OAuth / OIDC | code, state, PKCE, returnUrl | Generate or parse redirects/fragments |
| Business keys | ids in query/body (schedule, claim, patient, …) | In args or discover via prior call |
| Credentials / inputs | username, password, dates, filters | In args |
| Capture secrets | live passwords/JWTs in JSON | Never copy into source |

### Discovery over hardcoding

- Parse HTML for hidden inputs, meta tags, inline JSON, data-* attributes
- Parse JSON responses for ids/tokens needed by the next call
- Follow `redirectUrl` / `Location` with the same cookie jar
- For SPAs: if HTML has no useful URL, inspect linked JS or subsequent XHR in the capture for challenge/token endpoints
- URL `#fragment` values (`#code=`, `#access_token=`) exist only client-side — read from Location/URL string before GET

## Output contract (UiPath Invoke Code)

**Critical:** Runs inside UiPath **Invoke Code**.

- **No** `Imports`, `Option`, `Module`, `Class`, `Namespace`, wrapping `Sub`/`Function`
- Arguments = UiPath variables by name (In / Out / InOut)
- **Fully qualified types only** (`System.Net.HttpWebRequest`, `System.Text.RegularExpressions.Regex`, `Newtonsoft.Json.JsonConvert`, …)
- Body starts with `Try` (or statements) and handles errors into `errorMessage`
- Prefer `System.Net.HttpWebRequest` + `CookieContainer`
- Mirror real headers: `Accept`, `Content-Type`, `Referer`, `Origin`, `User-Agent`, custom headers from the capture
- `AllowAutoRedirect = False` when fragments or intermediate Set-Cookie hops matter; otherwise True is OK for simple HTML apps
- Do not log passwords or access tokens

### VB pitfalls

- No bare `Return` on its own line inside multi-line lambdas
- Do not split `As Some.Namespace.Type` across lines after `As`
- When using short type names in examples from other projects, **rewrite to fully qualified** for Invoke Code

### Common arg names (adapt to the site)

| Arg | Direction | Use |
|-----|-----------|-----|
| `cookies` | In / InOut | Session |
| `errorMessage` | Out | Failures |
| Token / header outs | In or Out | Whatever the capture uses |
| `sessionData` / form lists | InOut | WebForms hidden fields / rebuilt form pairs |
| Business fields | In | ids, dates, status codes |
| HTML / JSON outs | Out | `*Html`, `*Response`, DataTables, flags |

Match existing project naming when a folder already exists.

## Per-request checklist

- [ ] Method + URL (template dynamic segments)
- [ ] Document / xhr / form / json?
- [ ] Auth: cookies / bearer / custom headers
- [ ] Body encoding matches capture
- [ ] Must re-GET page for fresh hidden fields?
- [ ] Redirect / fragment handling?
- [ ] What to parse into Out args?
- [ ] Depends on prior Invoke Code outs?

## Deliverable

1. Short HTTP chain summary (bullets)
2. Complete Invoke Code VB body
3. Argument table (name, direction, source)
4. Notes: prior step required, HTML vs JSON, ignored noise

## Optional local references (style only)

If present in the workspace, you may mirror patterns from:

- JSON + Bearer/cookie APIs — e.g. `AxxessAutomation/`
- HTML + WebForms + form posts — e.g. `SandataAutomation/`

Always generalize to the **current capture’s host and flow**, not those product names.
