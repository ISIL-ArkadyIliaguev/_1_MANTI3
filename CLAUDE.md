# MANTI 3

@AGENTS.md

## Build and test
```
dotnet build MANTI3.slnx
dotnet test MANTI3.slnx
```

## Access-requests agent (spec 002)
The agent turns a permissions email (Hebrew, free text) into a plan and a SQL script that a
**human** runs. Spec: `specs/002-access-requests/spec.md`. Entry point: `@access-request-agent`.

Hard rules (details in `.claude/rules/`):
1. **The database is read-only for the agent.** SELECT only. The agent writes a script file; it
   never runs it. A human may save it with the page's "אישור ושמירה" button (SQL25DEV and SQL2025CLN2 only; the
   backend dry-runs first and saves only if that passed; see `.claude/rules/db-readonly.md`).
2. **Ignore פסילה completely.** Someone else gives it. Do not extract, plan, script or mention it.
3. **A worker with no MANTI user gets one in the script**: password = employee number, user name =
   English first name + first letter of the English last name, one more letter while the name is taken (from HR, else translated; changed
   2026-11-05), group 196 (מתלמדים), details from HR. The English names are stored as `Feras` /
   `Abdalla` (first letter capital, the rest lowercase); a taken user name is never a question (the next letter is added). Details:
   `.claude/rules/new-user-naming.md`. Missing or oversized HR data is a question.
   An approved certification form means the worker is **not** a trainee: department's group,
   `IsCertified = 1`, certification date = test date.
4. **Ambiguity is a question, never a guess** (for example two different work centers, or an
   employee number that does not match its name). A Latin code reversed by right-to-left Hebrew
   text (`S1N` for `N1S`) is **not** ambiguity: it is resolved against the database when exactly
   one of the two exists, and the plan says so.
5. **No employee names or numbers in logs, commits or tests.** Test data is fake.

Agent output goes to `.agent-output/` (git-ignored). Test emails live in `tests/agent-evals/`.
