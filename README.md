# SkillBox

Turns website traffic captures into **UiPath Invoke Code** (VB.NET) — no UI selectors.

## What matters here

The main file is **[agent-bob-skills.md](agent-bob-skills.md)**.

That skill tells AI agents how to read a BobScout capture and write UiPath-ready VB.NET. Paste the result into an **Invoke Code** activity and wire the arguments.

## Capture traffic

Use **[BobScout Desktop](https://github.com/BuildAgentBob/BobScout.DesktopApp)**:

1. Enter the website URL  
2. Start recording  
3. Do the login or action in the browser  
4. Stop and export the workflow JSON  
5. Give that JSON to an agent with this skill  

## Simple flow

```text
BobScout capture JSON  →  agent-bob-skills  →  UiPath Invoke Code VB
```

## Repo (brief)

| Item | What it is |
|------|------------|
| [agent-bob-skills.md](agent-bob-skills.md) | Skill for AI agents (start here) |
| [Samples/](Samples/) | Optional example VB for agents to match style |

This GitHub repo: https://github.com/BuildAgentBob/SkillBox
