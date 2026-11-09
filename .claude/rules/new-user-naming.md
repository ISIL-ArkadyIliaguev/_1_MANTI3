---
paths:
  - "specs/002-access-requests/**"
  - ".claude/agents/access-request-agent.md"
  - ".claude/skills/process-access-request/**"
  - "src/Manti3.Backend/**/AccessRequests/**"
  - "src/Manti3.Backend/**/Agent/AgentLookups.cs"
  - "tests/**/EnglishNamesTests.cs"
  - "tests/**/AccessScriptBuilderTests.cs"
---

# New MANTI user: names (decided 2026-11-05)

When the script creates a MANTI user (`User_Profiles` row):

1. **English names.** Take them from HR (`PRS_WORKERS_V6.FirstNameE` / `LastNameE`). Only when HR has
   no usable English name (empty, not Latin letters, longer than 15), translate the Hebrew name to
   English (common transliteration, Latin letters only) and say so in the plan.
2. **`EngFirstName` / `EngLastName`**: first letter capital, the rest lowercase (`Feras`, `Abdalla`),
   up to 15 characters. Not all capitals. A name of several words: each word starts with a capital
   (`Ben David`).
3. **`UserName` = English first name + the first letter of the English last name, and while that name is
   taken, one more letter each time** (changed 2026-11-08; it was the first 2 letters). `Feras` +
   `Abdalla` = `FerasA`; if `FerasA` exists then `FerasAb`; if that exists then `FerasAbd`; and so on until
   the name is free. The first letter of each part is capital and the rest lowercase. Letters only:
   no spaces, hyphens or apostrophes in the user name.
4. **`UserPassword` = the employee number** (unchanged).
5. **Uniqueness is checked against `User_Profiles`** (`UserName` is unique): the code reads which of the
   candidate names exist (case-insensitive, on the server the request was read from) and takes the first
   free one. Two new users in the same request never get the same name. If every name up to the whole
   last name is taken, the page says so and a person types a user name. The agent does not check or ask
   about user names.
6. **The code computes it, not the model.** `EnglishNames` (in `BuildScript/`) builds the user name and
   the stored English names from `newUser.engFirstName` / `engLastName` in `spec.json`. The agent
   never writes a user name or a password in `spec.json` or in SQL.
7. **A person may overwrite them in the results table** (decided 2026-11-05): the "שם משתמש" and
   "סיסמה" boxes are filled with the computed user name and the employee number, and what the person
   types is stored as `newUser.userName` / `newUser.password`. The server still checks it: user name
   1-50 letters, digits, dot, dash or underscore; password 1-10 characters with no spaces; and
   the user name must not be taken (checked when the script is rebuilt, and by the unique index when
   the script runs).

Existing users are never renamed: this applies only to a user created by the script.
