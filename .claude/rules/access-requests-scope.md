---
paths:
  - "specs/002-access-requests/**"
  - ".claude/agents/access-request-agent.md"
  - ".claude/skills/process-access-request/**"
  - "tests/agent-evals/**"
---

# Access requests: scope

**A worker who already has a MANTI user: add only** (changed 2026-11-08; the rule of 2026-11-05 was
"do nothing"). The agent may add a missing work center and copy the example user's screens to them,
and a person may add applications (screens, access permission only) in the page's table. Nothing
else: no removal or delete of any permission, no change of name, group, department, password,
certification or toolbar. The user code may be 3 digits or more: it is whatever `UserCode` holds.
Every insert is `WHERE NOT EXISTS`, so what the worker already has is untouched.

**Every worker is shown** (2026-11-08): each employee number that exists in HR or MANTI gets a row on
the page, new or existing, with or without a certification, with or without a clear request. A question
about one detail never removes the worker: write the worker with what is known and leave out only the
unclear item. Without an approved certification form a new user is a trainee (196): do not ask.

In scope:
1. Add the worker to a work center (one row in the work-center authorization table).
2. Copy the screen permissions of the named example user to the worker.
3. Create the MANTI user when the employee number has none: password = employee number, user
   name = English first name + first letter of the English last name, one more letter while taken (from HR, else translated;
   changed 2026-11-05), group 196 (מתלמדים), details from HR. Naming details (English names,
   capitalization, taken names): `new-user-naming.md`.

Out of scope:
- **פסילה**: ignore completely. Do not extract it, plan it, script it, or mention it.
- Anything that writes to the database. The agent never runs its script; only the human-operated
  "run script" button does (see `db-readonly.md`).

Decisions:
- Plan only what the email asks for. A work center and screen permissions are independent; do
  not assume both.
- If the email is unclear (two different work centers, names without numbers, a number that does
  not match its name), return a question. Never pick silently.
- A Latin code reversed by right-to-left Hebrew text (`S1N` for `N1S`) is not ambiguity: resolve
  it against the database as SKILL.md describes, and say so in the plan.
- A line "דוגמא <name> <number>" means: copy screens from that user. It needs no question. Its
  work centers are not copied; only the work centers named in the email are granted.
- Verify every employee number, work center and example user against the database before it
  appears in the plan. If the database is not reachable, say so and mark everything "not verified".
