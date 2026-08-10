# Agent Bob SkillBox

Turns website traffic captures into **UiPath Invoke Code** (VB.NET) — no UI selectors.

## What matters here

The main file is **[agent-bob-skills.md](agent-bob-skills.md)**.

Give an AI agent that skill plus a BobScout capture JSON. The agent discovers the **marked action** in the file and writes minimum Invoke Code for it.

## Capture traffic

Use **[BobScout Desktop](https://github.com/BuildAgentBob/BobScout.DesktopApp)**:

1. Enter the website URL  
2. Start recording  
3. Do the action in the browser  
4. Stop and export the workflow JSON  
5. Give that JSON to an agent with this skill  

## Simple flow

```text
BobScout capture JSON  →  agent-bob-skills.md  →  UiPath Invoke Code VB
```

## Suggested user prompt

```text
I attached a BobScout capture JSON.
Follow https://github.com/BuildAgentBob/AgentBobSkillBox/blob/main/agent-bob-skills.md
Automate the marked action in the JSON.
```

## Repo

| Item | What it is |
|------|------------|
| [agent-bob-skills.md](agent-bob-skills.md) | Skill for AI agents (start here) |
| [Samples/](Samples/) | Optional example VB for agents to match style |

GitHub: https://github.com/BuildAgentBob/AgentBobSkillBox
