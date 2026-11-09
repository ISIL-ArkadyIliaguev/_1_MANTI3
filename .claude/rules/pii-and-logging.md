# Personal data (employees)

- Employee names and employee numbers are personal data. They must not appear in logs,
  exception messages, commits, issues, or test fixtures that are committed.
- Tests use fake data (for example employee `10001`, `10002`, named `Worker A`, `Worker B`).
  The original email from the task is never committed with its real numbers.
- Agent output (`.agent-output/`) holds real data and is git-ignored. Do not copy its contents
  into other files.
- When a request does not need names, work with employee numbers only.
