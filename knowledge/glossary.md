---
type: reference
title: Glossary — agentic development
updated: 2026-10-06
---

# Glossary — agentic development

Bilingual reference for the core terms, for learning the modern AI-assisted stack.

| Term | English (short) | עברית |
| --- | --- | --- |
| **Agent** | An LLM running in a loop with tools to reach a goal. | מודל שפה שרץ בלולאה עם כלים כדי להשיג מטרה. |
| **Tools** | Functions the agent can call (read a file, run a command, call an API). | הפעולות שהסוכן יכול להפעיל. |
| **Agentic loop** | The cycle: think → call a tool → observe the result → repeat until done. | המחזור: לחשוב → להפעיל כלי → לראות תוצאה → לחזור. |
| **Context** | Everything the model currently "sees": instructions + files + history + tool results. | כל מה שהמודל רואה כרגע. |
| **Instructions** | Standing guidance that shapes behavior (e.g. `AGENTS.md`, `CLAUDE.md`). | הנחיות קבועות שמעצבות התנהגות. |
| **Skills** | On-demand packages of knowledge/instructions (Markdown + frontmatter), e.g. `/speckit-*`. | חבילות ידע/הנחיות שנטענות לפי צורך. |
| **MCP** | Model Context Protocol — a standard for connecting agents to external tools and data via "servers". | פרוטוקול לחיבור הסוכן למקורות חיצוניים. |
| **Spec-Driven Development** | Write spec → plan → tasks first; the agent implements against them. | כותבים אפיון→תכנון→משימות, והסוכן מממש מולם. |
| **OKF** | Open Knowledge Format — portable Markdown+YAML knowledge bundles. | פורמט פתוח לאריזת ידע כקובצי Markdown. |

Related: [Methodology](methodology-okf-speckit.md)
