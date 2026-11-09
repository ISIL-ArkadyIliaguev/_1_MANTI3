---
name: process-access-request
description: Use when given an email (Hebrew, free text) asking to give workers MANTI permissions - add workers to a work center, copy permissions from an example user, or open a user. Produces a verified plan and a SQL script for a human to run. Never writes to the database. Ignores פסילה.
allowed-tools:
  - Read
  - Grep
  - Glob
  - Bash
  - Write
---

# Process an access request

Input: the text of one email (possibly a forwarded thread), **or a PDF certification form**
from the training system (saved in `.agent-output/inputs/`; how to read it and its fields:
`references/parsing.md`, "Input type 2"). Output: files in
`.agent-output/<yyyyMMdd-HHmm>/` and a short summary. Scope and rules: `.claude/rules/`,
spec: `specs/002-access-requests/spec.md`.

**The input file may hold several parts as ONE request.** The website joins what the person gave:
an optional block `=== Text typed by the person who sent this request ===` (notes and instructions
that apply to everything), then `=== Mail 1 of N ===`, `=== Mail 2 of N ===`, ... Treat all parts
together: the union of the workers, work centers and example users named anywhere. A worker named
in several parts is one worker. If the parts contradict each other (two different work centers for
the same worker, two example users), that is a question, not a choice. Never plan from only the
first mail.

The file can also end with `=== Attached PDF forms ... ===` and a list of PDF paths under
`.agent-output/inputs/`. Those are certification forms of the same request: read each one with
`pdftotext` (`references/parsing.md`, "Input type 2") and treat it like the mails: one request,
the union of everything. A worker certified by a form and also named in a mail is one worker,
planned once (certified, per the rules for certification forms).

## Workflow

### 1. Parse
Extract, from the whole thread:
- **workers**: employee number (the key) and name, for every person who should receive permissions.
- **workCenters**: the work-center codes to add the workers to (for example `S1N`).
- **exampleUser**: the employee the email says to copy permissions from ("דוגמא ...").
- **requested actions** per worker, from this closed list only:
  `add-work-center`, `copy-permissions-from-example`.
- **ambiguities**: anything unclear, as a question.

**Ignore פסילה.** Do not extract it, plan it or mention it, anywhere in the output.

Work center and screen permissions are independent. Plan only what the email asks for.
Details and examples: `references/parsing.md`.

### 2. Verify against the database (read-only)
For every employee number, work center and example user, query the database with
`sqlcmd -S <server> -d Manti_IscarData -E -W -f o:65001 -o "<request folder>/q<N>.txt" -Q "SELECT ..."`
(`<server>`: the one the prompt names, e.g. `SQL25DEV` or `SQL2025CLN2.iscar.com,2433`; default `SQL25DEV`)
(one SELECT per call; in web mode batch them with `;`, see "Web mode"), then Read the file. **Write the `-o` path with backslashes**
(`.agent-output\<folder>\q<N>.txt`): with `/` sqlcmd parses `/U...` as `-U` and fails with
"-E and -U/-P are mutually exclusive". This is not a connection problem, so do not report the
database as unreachable; fix the path and run the SELECT again. The console garbles Hebrew; the UTF-8 file does not,
and the files double as evidence. **`-o` must point inside the request's `.agent-output` folder**,
never to a temp or scratch directory: the results contain employee data. Only the
servers named in `db-readonly.md` (`SQL25DEV`, `SQL2025CLN2`) are allowed; the hook blocks any other server.
The schema is in `knowledge/permissions-schema.md`.

- **Work-center code reversed by right-to-left text:** look up the code and its reverse together
  (`WHERE WorkCtrCode IN ('S1N','N1S')`). Exactly one exists: use it and write in the plan
  "S1N read as N1S (reversed in Hebrew text)". Both exist, or neither: a question.
- **Employee number vs. name:** compare the name written next to the number with
  `UserFirstName` / `UserLastName`. If they clearly do not match (possibly a reversed number),
  it is a question and nothing is planned for that worker.
- Employee number with no MANTI user: plan **"create user"** from the HR data (template in
  `references/script-templates.md`): password = employee number, **user name = English first name
  + first letter of the English last name, one more letter while that name is taken** (`Doron` + `Shapira` = `DoronS`,
  then `DoronSh`...; the website does this, you do not pick or check it), group 196
  (מתלמדים) — **except an approved certification form**: then the department's group and
  certified (`references/parsing.md`, "Input type 2"). The English names come from HR
  (`FirstNameE` / `LastNameE`, filled for every active worker); the lookups file already lists
  them (section `englishNames`). Only when HR has no usable English name: **translate the Hebrew name to
  English yourself** (common transliteration, Latin letters only) and say so in the plan. Check the HR
  row exists, the department maps to MANTI, names ≤ 15 characters (Hebrew and English), phone
  ≤ 8. A taken user name is **not** a question: the website adds a letter of the last name until the name
  is free. Any failed check is a question, and nothing is planned
  for that worker. The new user also gets the requested work center and screens in the same script, plus
  the defaults in `references/new-user-defaults.md` (work centers of the department, default
  screens by department majority). Read that file for every new user; it is the single source
  of the defaults.
- **Worker who already has a MANTI user (a `User_Profiles` row with that `UserCode`): do nothing.**
  Plan nothing for them, whatever the email asks (work center, screens, certification). Not a question.
- Worker or example user with `UsersStatus = '1'` (inactive): return a question, plan nothing
  for that worker.
- Work center or example user that does not exist, or a work center with `WorkCtrDisplay = 0`:
  mark invalid, do not plan it.
- Permission already present: mark "already granted" in the plan, **and still include it in the
  script**. The state may change between planning and running, and every statement checks
  `NOT EXISTS` when it runs, so a present permission is a no-op and a missing one is restored.
- If the database is not reachable or `knowledge/permissions-schema.md` does not exist: say so,
  and mark every item **"not verified"**. Do not guess table names.

### 3. Plan
One row per worker and action: what is missing and what will be added. List every open question
(for example a work center written two ways) and stop for those items.

### 4. Script
Write `script.sql` with the statements that apply the plan. The script is a file for a human to
review and run. **Never run it.** Templates: `references/script-templates.md`.

- Write statements (INSERT) only for items that are verified and have no open question.
- Cover **every requested action for every verified worker**, including "already granted" ones:
  every worker gets its work-center statement and one statement per screen of the example user.
  Never drop a worker from the script because they already have everything.
- End the script with a check SELECT that covers all workers of the request.
- When items are "not verified" (no database, or no schema file), `script.sql` holds **only the
  SELECT queries** a person runs to verify them by hand. No INSERT at all, not even as a comment
  or a template, so a draft can never be mistaken for a ready script.

### Web mode (only when the prompt says WEB MODE)
Typing a long SQL script is the slowest step, and the SQL comes from fixed templates, so in web
mode **you do not write `script.sql`**. You write `spec.json`: the verified facts. The website
builds `script.sql` from it (same templates as `references/script-templates.md`, which you do not
need to read in web mode).

```json
{
  "requestId": "<input file name without extension>",
  "example": { "userCode": "10005", "userId": 2412,
               "screens": [ { "objId": 742, "name": "<AppTbToolTip>" } ] },
  "workers": [
    { "userCode": "10001",
      "userId": 5001,
      "newUser": { "firstName": "<HR, trimmed>", "lastName": "<HR, trimmed>",
                   "engFirstName": "<HR FirstNameE, or your translation>", "engLastName": "<HR LastNameE, or your translation>",
                   "phone": "3006", "email": "<HR>",
                   "departmentId": 31341, "groupId": 196, "certificationDate": "yyyymmdd" },
      "branchId": 1,
      "workCenters": [ { "id": 926, "code": "N1S" } ],
      "copyScreens": true,
      "defaultScreens": [ { "objId": 1712, "name": "<name>", "controlId": "<30 chars of 0/1>" } ],
      "certifyExisting": { "groupId": 146, "date": "yyyymmdd" } }
  ]
}
```
- **A worker who already has a MANTI user: add only (changed 2026-11-08; before that: do nothing).**
  Write them with `userId`, `branchId` (from the users lookup), `name` (HR or MANTI first + last name,
  display only) and what the email asks to **add**: `workCenters` (verified ones) and/or `copyScreens`
  (+ `example`). The website adds only what is missing (`INSERT ... WHERE NOT EXISTS`), so an
  "already granted" item is harmless. **Never** delete or remove a permission, never change their
  details, group, certification or toolbar, and no `newUser`/`defaultScreens`/`certifyExisting` for
  them. A person may also add applications (`addScreens`) on the page; you do not write it. An
  existing worker the email asks nothing for: write `userId` only (the script then changes nothing
  for them). `workers` must not be empty. A worker whose code is 3 digits is as valid as a 5-digit one:
  the code is whatever `User_Profiles.UserCode` holds.
- `newUser` (create it, only after every HR check passed); `userId` marks an existing user to leave alone. `engFirstName` / `engLastName` are required for a new user (Latin
  letters, up to 15); the website computes the user name from them (first name + first letter of the
  last name) and also stores them in `EngFirstName` / `EngLastName`. `certificationDate` only for a certified new worker (group = the
  department's group); `certifyExisting` only for an existing worker certified by a form.
- `example` and `copyScreens: true` only when the email asks to copy screens. `example.screens` lists
  **every** screen (`ObjId` + name) of the example user. `workCenters` only the verified ones the email
  names. `defaultScreens` only for a **new** user, per `new-user-defaults.md`. `branchId` is the
  worker's branch.
- **Every worker gets a row (decided 2026-11-08, repeated by the user).** Every employee number in
  the request that exists in HR or in MANTI goes in `workers`, **in any case**: a new user (a number
  with no MANTI user; trainee, group 196, unless an approved certification form says otherwise), or
  an existing user (`userId`, even when nothing is asked for them), whether or not there is a
  certification and whether or not the email says what to do. **Never** leave a worker out of
  `spec.json` because of a question, a missing certification, or an unclear permission: write the
  worker with what is known and leave out only the unclear item (the work center or the screens),
  and put `"skip": true` only when the worker themself is uncertain (a gate failed, the number
  does not match the name). Do not ask "trainee or certified?": without an approved form it is a
  trainee. The page shows every row and the person completes it. The question goes in your short
  reply. Only a number found in neither HR nor MANTI has no row.
- Numbers are numbers, ids are ids from your SELECTs: do not invent or round. A request with no
  worker row at all (no number exists anywhere): write no `spec.json`, and write a short
  `script.sql` with verification SELECTs only, as before.
- **No `plan.md` in web mode**: the page does not show it. The evidence is the `q<N>.txt` result files
  and `spec.json`; open questions and unverified items go in `explanation.md` under `**לא יבוצע:**`.
- **Lookups file (the main speed-up).** When the prompt names `.agent-output/inputs/<stamp>-lookups.md`,
  the website already ran the standard verification SELECTs on SQL25DEV (users and names taken, HR rows
  with the mapped department and branch, the work centers and their reversed forms, the example
  candidates' screens with duplicates, the default-screen peers and trainee bits). **Read it in the
  same turn as the request and the reference files, and do not re-run those queries.** It is your
  database evidence: a number or code with no row there does not exist. Its numbers are only
  candidates found in the text; you still decide who the workers are, who the example is, and every
  question. Query yourself only for what it does not cover (a number found only inside a PDF, the
  certified-worker group, another default screen), in one batched call.
- **Speed: batch the queries** (when there is no lookups file, or for gaps). Every extra round costs a model turn (5-10 s). Run **one** `sqlcmd`
  call with several SELECTs separated by `;` (one output file `q1.txt`, several result sets, read
  once) for everything that does not depend on an earlier result: users, departments and branches of
  all worker and example codes, the work centers, the HR rows, the existing user names. Then **one**
  more call (`q2.txt`) for what needed those ids: the example's screens (and duplicates), the
  default-screen peers and trainee bits, the HR departments' branches. Aim for **2 query calls and
  2 reads in total**, then write `spec.json` in **one** turn (**web mode: no `explanation.md`**, decided
  2026-11-05: the page does not show it and it cost about 10 s; put anything not scripted, and every
  open question, in your short reply instead, in Hebrew, at most 5 lines; reply just `done` when every
  worker who needs a new user is in `spec.json`). Every SELECT
  still starts with `SELECT` and contains no write word (the hook blocks anything else). Do not
  re-read a file you already read, and do not read `script-templates.md` in web mode.

### 5. Explanation (for the person who runs the script)
Write `explanation.md`: what the script will do, in plain Hebrew, for someone who does not read
SQL. The website shows it first, above the plan. Derive it **from `script.sql`** (what the
statements really do), or in web mode **from `spec.json`** (the script is built from it), not
from the email, so the two cannot disagree.

- **No heading**: the page already shows the title "מה הסקריפט יעשה" above the file. Start
  directly with the first sentence or bullet.
- One bullet per worker per action, in this form, with employee numbers (no names):
  - `**עובד 11111** יצורף למרכז עבודה 777 (N1S)`
  - `**עובד 11111** יקבל 5 מסכים מהמשתמש לדוגמה 22222: 1712, 877, ...`
  - `**עובד 11112** כבר משויך למרכז עבודה 777 - לא ישתנה דבר`
  - `**עובד 11113** אין לו משתמש ב-MANTI: ייפתח משתמש חדש (שם משתמש וסיסמה = מספר העובד, קבוצת מתלמדים)`
- Say plainly what will **not** happen: items with an open question or "not verified" are listed
  under a line `**לא יבוצע:**` with the reason in a few words.
- A last line states the nature of the script: it only **adds**, deletes nothing, and runs inside a
  transaction that ends with ROLLBACK until the person changes it to COMMIT (only if the script
  really does so).
- When the script holds only verification SELECTs, say so in one sentence and write no bullets
  about permissions.
- No SQL, no table or column names, no פסילה. At most 15 bullets; group a long list of workers
  with the same action in one bullet ("עובדים 11111, 11112, 11113 יצורפו למרכז עבודה 777").

## Output
`.agent-output/<yyyyMMdd-HHmm>/plan.md` (parsed request, verification result, plan table,
open questions, **evidence**), `.agent-output/<yyyyMMdd-HHmm>/script.sql` and
`.agent-output/<yyyyMMdd-HHmm>/explanation.md` (step 5).

**Evidence section in `plan.md`:** every SELECT you ran, exactly as run, the `q<N>.txt` file
that holds its result, and the result rows. Count the SELECTs from those files, not from memory.
To correct `plan.md`, rewrite the whole file with Write (Edit, sed and python are not available). Every id and fact in the plan and the script must be traceable to a row there. A reviewer
(or the calling session, which cannot see your tool calls) checks the plan against it.
Final reply to the caller: a summary of at most 15 lines - counts, open questions, file paths.

**Never overwrite an earlier request's output.** Before writing, check with Glob whether the
folder already exists; if it does, add `-2`, `-3`, ... to the name. If the current time is not
known, say so in `plan.md` instead of guessing a precise one.

## Sections owned by the team
- `TODO(A)`: `knowledge/permissions-schema.md` (tables, columns, how to read current permissions).
- `TODO(B)`: `references/parsing.md` (extraction rules, ambiguity cases, sample emails).
- `TODO(C)`: `references/script-templates.md` (the INSERT templates and the copy-permissions diff).
