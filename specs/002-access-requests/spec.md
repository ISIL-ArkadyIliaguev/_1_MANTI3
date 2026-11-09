# Feature Specification: Access requests (email to permission change plan)

**Status**: Draft
**Constitution**: v2.1.0
**Scope of this iteration**: API only. No frontend page yet.

## Summary
The person in charge of MANTI permissions receives free-text Hebrew emails such as "give these
workers MANTI permission for work center S1N, like user 10005". Requests for פסילה permission
appear in the same emails but are handled by someone else; MANTI 3 ignores them completely
(see FR-011).
MANTI 3 receives the email text, extracts the request with an LLM, checks every extracted fact
against the MANTI database (read only), and returns a **change plan**: for each worker, what is
missing and what must be added. A human reviews the plan and applies it. MANTI 3 never writes to
the MANTI database.

## User Scenarios & Testing

### User Story 1 - Parse an email into a structured request (Priority: P1)
The responsible person submits the email text and gets back the workers (name, employee number),
work centers, permission types and the example user mentioned in the email.

**Why this priority**: Everything else depends on a correct structured request.

**Independent Test**: Post the sample email (`tests/agent-evals/emails/sample-01.md`: 10001 /
10002 / 10003 / 10004, example 10005) and compare the result to the expected JSON. No database needed.

**Acceptance Scenarios**:
1. **Given** the sample email, **When** it is submitted, **Then** four workers with the listed
   employee numbers, the MANTI permission and work center S1N, and example user 10005 are
   returned. The mention of פסילה does not appear in the result.
2. **Given** an email that shows work center "N1S" in one place and "S1N" in another (the same
   code, reversed by right-to-left Hebrew text), **When** it is verified, **Then** the code that
   exists in the database is used and the plan notes the reversal. Only if both or neither exist
   is it a question.

### User Story 2 - Validate against the database (Priority: P1)
Each extracted employee number, work center and example user is looked up, and the current
permissions of each worker are read.

**Why this priority**: Prevents plans built on typos or non-existent entities.

**Independent Test**: Integration test on the test database with known users.

**Acceptance Scenarios**:
1. **Given** an employee number that has no MANTI user, **When** validated, **Then** the plan
   creates the user from HR (user name = password = employee number, group 196) and grants the
   requested permissions in the same script; if the HR data fails a check, it is a question.
2. **Given** a work center code that does not exist, **When** validated, **Then** the request is
   marked invalid for that item and the plan does not include it.
3. **Given** a worker who already has the permission, **When** validated, **Then** the plan shows
   "already granted", and the script still contains the guarded statement, so a permission removed
   between planning and running is restored and an existing one is left as is.

### User Story 3 - Build the change plan and a script for a human (Priority: P2)
For every worker the plan lists the missing permissions, including those copied from the example
user, and a SQL script is generated for a person with write access to run.

**Why this priority**: This is the deliverable the responsible person acts on.

**Independent Test**: Unit test comparing the plan and script to the expected output.

**Acceptance Scenarios**:
1. **Given** a validated request, **When** the plan is built, **Then** each worker has the list of
   additions and each ambiguity is listed as a question to answer, not a guess.
2. **Given** an unresolved ambiguity, **When** the script is requested, **Then** no script is
   produced for the affected items.

### Edge Cases
- Email with no worker numbers (names only): match by name, return candidates, never pick one silently.
- Email with a forwarded chain (the sample has two): use the whole thread, report conflicts.
- Hebrew and English mixed, numbers with spaces or punctuation.
- Same worker listed twice; worker without permission to be granted by this requester.
- Email that asks only for "reporting" on a work center (no screens), or only for screens (no
  work center): plan only the requested kind. A vague phrase such as "הרשאות לדיווח" is flagged,
  not expanded into everything.
- LLM returns invalid JSON or times out: `502`/ProblemDetails, nothing is guessed.

## Requirements

### Functional Requirements
- **FR-001**: `POST /api/access-requests/parse` accepts the email text and returns a structured
  request (workers, work centers, permission types, example user, plant/manager change if stated).
- **FR-002**: Every extracted field carries a confidence level; low confidence or conflicting
  values are returned as questions, not resolved silently.
- **FR-012**: A request can contain three kinds of action, in any combination: (1) create a new
  MANTI user for an employee who has none, (2) copy the screen permissions of the named example
  user to the worker, (3) add the worker to a work center. The plan lists each action per worker.
- **FR-015**: When a worker has no MANTI user, the script creates one (decided 2026-10-28):
  user name = employee number, password = employee number, user group **196 (מתלמדים)**. The new
  user gets the requested work center and screens in the same script, plus defaults kept in one
  editable file (`references/new-user-defaults.md`): every displayed work center of the user's
  department (switched **off** for now: only the work center named in the email), and default
  screens (today 1712 "spc/דיווח") given when most active users of the
  department have them, with the bits most common among the department's trainees. An example
  user's version of a screen wins over the default.
- **FR-013**: For a new user, the details (first and last name, department, phone, email) come
  from the HR source MANTI 2 uses to prefill a new user. A missing or unmappable field (no HR row,
  no MANTI department, a name longer than 15 characters, a phone longer than 8) is a question.
  A taken user name is not: the user name is the English first name + the first letter of the
  English last name, and the next letter is added while the name exists (changed 2026-11-08).
- **FR-014**: Copying permissions means the plan shows exactly which screens and bits the example
  user has that the worker lacks; it does not copy anything the email did not ask for.
- **FR-016**: An approved certification form ("אישור הסמכה: מאשר" and "בוצע בהצלחה") makes the
  worker certified and no longer a trainee: `IsCertified = 1`, `CertificationDate` = test date,
  and the user group = the most common group of the worker's department (excluding 196 and 0).
  A new user is created in that group; an existing user in 196 is moved; other groups stay.
- **FR-017**: A local web page (`/access-requests` in the frontend, `POST /api/access-requests`
  in the API) takes the email text or a PDF form, runs the agent, and shows its summary, plan and
  script with a copy button. It never runs the script. Development environment only, one run at a
  time, until authentication exists.
- **FR-011**: פסילה permission is out of scope: it is given by someone else. When an email
  mentions it, the system ignores it completely: it is not extracted, not planned, not scripted
  and not reported.
- **FR-010**: The two kinds of permission are independent: (a) screen/application permissions
  (e.g. MANTI) and (b) work-center authorization, which is a single table where a row (user,
  work center) means the user is checked for that work center. A request may ask for either or
  both.
  The system MUST plan only what the email asks for, and MUST NOT assume both. When the email
  does not make clear which kind is wanted, it is returned as a question.
- **FR-003**: Every extracted employee number, work center and example user is verified against
  the MANTI database before it appears in a plan.
- **FR-004**: The plan lists, per worker, current permissions, missing permissions, and whether a
  new user must be created.
- **FR-005**: The system generates a SQL script for the missing permissions. It only returns the
  script text; it never runs it.
- **FR-006**: MANTI 3 never changes the MANTI database: SELECT only, no migrations (constitution).
  The database account used has no write permission.
- **FR-007**: Each request, the parsed result, the plan and the generated script are recorded in
  an audit log kept outside the MANTI database. [NEEDS CLARIFICATION: where is the audit log stored,
  since MANTI 3 has no database of its own yet?]
- **FR-008**: Personal data sent to the LLM is limited to what is needed for parsing.
  [NEEDS CLARIFICATION: company policy on sending employee names and numbers to an external LLM]
- **FR-009**: Failures (LLM, database) return ProblemDetails without exception details.

### Key Entities
- **AccessRequest**: the submitted email, the parse result, status.
- **RequestedWorker**: name, employee number, flags (new user, already has permission).
- **RequestedPermission**: kind (screen or work-center), screen/application (e.g. MANTI) and
  access level (view/edit) for screens, work center for work-center kind, source (stated, copied
  from example).
- **ChangePlan**: per-worker additions, open questions, generated script.

## Success Criteria
- **SC-001**: The sample email is parsed with all 4 employee numbers and the example user correct.
- **SC-002**: No plan item references an employee or work center that was not verified in the database.
- **SC-003**: The responsible person needs to type nothing for a clean email, only review the plan.
- **SC-004**: An attempt to write to the MANTI database with the MANTI 3 account fails.

## Assumptions
- Email text is supplied by the caller (Outlook rule/add-in is a later step).
- Work-center authorization is a (user, work center) row; the exact tables are confirmed by
  scaffolding the database (discovery step).
- Screen permission ("MANTI") is one authorization row per screen (ObjId) with control bits for
  what the user sees and can edit. [NEEDS CLARIFICATION: ObjId of the MANTI screen(s) and which
  bits mean view vs. edit]
- Work-center authorization is one table; granting means adding the (user, work center) row.
- Screen permissions are granted by copying them from the example user named in the email
  (MANTI 2 does this with its "copy authorizations from user" function).
- Creating a user in MANTI 2 has side effects beyond one row (user id, group, password handling).
  [NEEDS CLARIFICATION: what the user-creation procedure does, so the generated script reproduces it]
- פסילה is out of scope and has nothing to do with MRB.
- Frontend approval page, automatic execution and Outlook integration are out of scope here.

## Known gaps (recorded per constitution VI)
- **Authentication/authorisation**: not implemented; this endpoint exposes personal data and
  must not be deployed beyond a developer machine until it is.
- **Read-only database account**: still to be requested.
