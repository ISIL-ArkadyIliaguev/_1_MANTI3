# Parsing rules (owner: B)

Status: skeleton. B fills this in.

## Known rules (from the spec)
- Employee number is the key. A name without a number is returned as a candidate list, never picked.
- Take the whole thread. If two places disagree on something real (two different work centers,
  two numbers for one name), return both and ask. Do not choose.
- **Hebrew emails reverse Latin codes.** The emails are right-to-left; when they are copied, a code
  such as `N1S` can arrive as `S1N`. This is not the sender's typo. Collect each work-center code
  together with its reverse (`S1N` ↔ `N1S`) and let verification decide (see SKILL.md step 2).
  In the sample email, `N1S` and `S1N` are the same work center.
- Employee numbers can be affected the same way, which is why every number is checked against the
  name written next to it.
- "Example user" lines ("דוגמא <name> <number>") are an explicit request to **copy screens** from
  that employee. Do not ask whether to copy them. **Only screens: the example's work centers are
  never copied** (decided 2026-10-28); work centers come only from the email.
- A vague phrase such as "הרשאות לדיווח" in another part of the thread does not cancel an explicit
  "דוגמא" line. Flag it only when the email has no example user and no work center.
- **Ignore פסילה** wherever it appears.

## Input type 2: Blossom certification form (PDF)
Some requests arrive as a PDF report from the training system (`iscar.blossom-kc.com`), e.g.
"דוח ביצוע עבור <name> - מבחן מעשי לביקורת גמר- הערכת מנהל (<date>) (הורץ על ידי <evaluator>)".
A `blob:` link cannot be opened; the PDF must be saved to `.agent-output/inputs/` first.

Extract it **twice** (tested 2026-10-28 on a real form):
```
pdftotext -enc UTF-8 -raw "<file>.pdf" "<request folder>/form-raw.txt"
pdftotext -enc UTF-8      "<file>.pdf" "<request folder>/form.txt"
```
- `form-raw.txt` keeps each question and its answer **on one line**, but Hebrew letters come out
  **reversed** (numbers and Latin codes are correct). Use it for the fields below, matching the
  labels **exactly as written reversed** here.
- `form.txt` has correct Hebrew but separates answers from questions. Use it only for the title
  (worker's name, date, evaluator).
- Do not use `-layout`: it splits the table into unrelated columns.
- If both files are empty (a scanned image), stop and say so.

Fields in `form-raw.txt` (reversed label, then what it means):
| Line in `form-raw.txt` | Field | Real label |
|---|---|---|
| `<number> :ןחבנה דבועה לש דבועה רפסמ תא ןייצ` | employee number | ציין את מספר העובד של העובד הנבחן: |
| `<code> ?האשרהה תשרדנ הדובע זכרמ הזיאל` | work center code | לאיזה מרכז עבודה נדרשת ההרשאה? |
| `רשאמ הכמסה רושיא` | **gate** passed | אישור הסמכה מאשר |
| `החלצהב עצוב` | **gate** passed | בוצע בהצלחה |

From `form.txt`, first line: `דוח ביצוע עבור <name> - ... (<dd/mm/yyyy>)(הורץ על ידי <evaluator>)`:
the worker's name (check it against HR / `User_Profiles`), the test date and the evaluator.
The final grade is the number on the line after `ציון` in the summary.

Rules:
- **Certify and grant the work center only if both gates pass.** If a gate fails the worker is **not
  certified**, but they still get a row on the page (decided 2026-11-08): write them in `spec.json`
  with everything known (HR names and department, `userId` if they already have a MANTI user,
  otherwise `newUser` as a trainee: group 196, no `certificationDate`), **no `workCenters`**, and
  `"skip": true` so the row is shown unchecked. The person decides on the page whether to save it
  and may add the work center by hand. Name the failed gate in your short reply.
- The scores (1–6 per item, the final grade) are not a gate. Show the final grade in the plan.
- By default a form has **no example user**, so no screens are copied: a new user gets the
  new-user defaults (`new-user-defaults.md`). An existing user gets nothing from a certification form
  (2026-11-05, still true): no group or certification change. Only what the email asks to add (work
  center, copy of an example user's screens) is added to a worker who already has a MANTI user
  (2026-11-08).
- **If the form says otherwise** (names screens, an example user, more work centers), follow what
  is written (decided 2026-10-28).
- **A certified worker is no longer a trainee** (decided 2026-10-28). When both gates pass:
  `IsCertified = 1`, `CertificationDate` = the test date from the title, `CertificationDateEnd`
  untouched; the user group is the department's group (`new-user-defaults.md`, "Certified
  worker"), never 196. A new user is created directly in that group. An existing user is not
  touched (no certification update, no group change: 2026-11-05).
- The form mentions "פסילה" inside item texts (e.g. AS400 items). Ignore it like everywhere else.

## TODO(B)
- Phrases that mean "add to work center" and "copy from example".
- Handling of several work centers in one email, and of different workers for different centers.
- Edge cases: numbers with spaces or punctuation, duplicate workers, mixed Hebrew and English.
- 4-5 sample emails with the expected result (the first one is in `tests/agent-evals/`).
