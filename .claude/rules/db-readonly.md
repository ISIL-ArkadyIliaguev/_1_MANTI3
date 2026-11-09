# Database is read-only

- Only `SELECT` queries touch the MANTI database. No `INSERT`, `UPDATE`, `DELETE`, `MERGE`,
  `DROP`, `ALTER`, `TRUNCATE`, `CREATE`, and no `EXEC` of a stored procedure that writes.
- Use a SQL account that has no write permission. **Until one exists (2026-10-28), the only
  account can write.** Reading (SELECT only) is allowed on two named servers, approved 2026-11-05:
  `SQL25DEV` (the default) and `SQL2025CLN2.iscar.com,2433`, both database `Manti_IscarData`. The
  list is `Agent:Servers` in the API settings, and the hook `guard-sql.ps1` allows the same two; the
  browser sends only a name from that list. The hook blocks every other server. Production is
  off-limits until the read-only account exists.
- **Writing is `SQL25DEV` (Windows authentication) and `SQL2025CLN2` (SQL login, `AllowWrite` in
  `Agent:Servers`, approved by the user 2026-11-08), nowhere else.** The script runs on the server the
  request was read from (`server.txt` in the request folder); a request prepared from any other server shows
  no "run script" button, and the backend refuses to run its script. The agent still only reads.
- A generated script (for example `INSERT` of a work-center row) is a **text file for a human
  to review and run**. Write it to `.agent-output/`, never execute it. **The agent never runs it.**
- **One exception, approved 2026-11-04, changed 2026-11-05:** the "אישור ושמירה" button on the
  access-requests page (before: separate dry-run and confirm buttons). A human presses it once;
  the backend (not the agent) rebuilds `script.sql` from the table values, runs it on `SQL25DEV`
  (Windows authentication) or `SQL2025CLN2` (SQL login) only, as a dry run first (rolled back), and saves only if the dry
  run succeeded on the same script. The person no longer sees the dry-run result before the save. `AccessScriptGuard` rejects anything but the known INSERT/UPDATE
  statements, and the browser sends a request id, never SQL. Do not widen this to other servers,
  other tables, or to the agent.
- Connection strings and passwords come from user-secrets or environment variables. Never put
  them in a command line, a file in the repo, or the chat.
- The hook `.claude/hooks/guard-sql.ps1` blocks write SQL through `sqlcmd`; it is one layer only.
  These rules and the read-only account are the other layers.
