# Agent evals (manual)

Checks the `access-request-agent` against sample emails. Run by hand for now.

## Run
1. Start Claude Code in `C:\_1_MANTI3`.
2. Ask: `@access-request-agent` followed by the content of `emails/sample-01.md`.
3. Compare the reply and `.agent-output/<latest>/plan.md` with `expected/sample-01.json`.

## Pass criteria for each sample
- Workers, example user and work centers match `expected`.
- Every `mustAsk` item appears as a question, not a decision.
- Nothing from `mustNotAppear` appears anywhere in the output, including `plan.md`.
- Without a database connection: every item is "not verified", and `script.sql` holds only
  SELECT queries for manual verification. The word INSERT must not appear in it at all.
- With a database: an employee with no MANTI user gets a "create user" step (user name = password
  = employee number, group 196) followed by the work center and screens, all in one batch. If the
  HR data fails a check, it is a question and nothing is scripted for that worker.

## Data
All employee numbers and names here are fake (`10001`...). Never commit a real email.
Add new samples as `emails/sample-NN.md` + `expected/sample-NN.json`.
