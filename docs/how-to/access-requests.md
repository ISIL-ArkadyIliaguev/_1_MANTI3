# Access requests: how to run it

Spec: `specs/002-access-requests/spec.md`. The agent prepares; a person runs the script.

## Start (developer machine, once per session)
Two terminals in `C:\_1_MANTI3`:
```
dotnet run --project src\Manti3.Backend\Manti3.Backend.Api --launch-profile https
dotnet run --project src\Manti3.Frontend --launch-profile https
```
Open `https://localhost:7296/access-requests` (the API is on https://localhost:7127; both need the trusted
dev certificate: `dotnet dev-certs https --trust`). Or just run `.\scripts\dev.ps1 restart`.
From Outlook: see `outlook-addin.md`.

Requirements on that machine: Claude Code (`claude.cmd` on PATH, logged in), `sqlcmd`,
`pdftotext` (comes with Git for Windows), and Windows access to `SQL25DEV`.
Settings: `Agent` section in `src/Manti3.Backend/Manti3.Backend.Api/appsettings.Development.json`.

## Another PC (each PC runs its own copy)
1. Copy the whole repo folder to the PC (zip or git remote; `.agent-output` is not needed).
2. Install .NET 10 SDK, Git for Windows, `sqlcmd`, and Claude Code (run `claude` once and log in).
3. From the repo root: `.\scripts\setup-new-pc.ps1`. It checks all of the above (including Windows access to
   `SQL25DEV`), trusts the dev certificate and builds the SQL helper. Fix any `[MISSING]` and run it again.
4. `.\scripts\dev.ps1 restart`. The script passes the repo folder and the Git bash path to the API itself,
   so `appsettings.Development.json` needs no editing.
5. Outlook: add the add-in from file (`outlook-addin.md`). Each PC needs its own installation.

## Workers who already have a MANTI user (2026-11-08)
They appear in the table too (name only, marked "משתמש קיים"). For them you can **add** work centers and
applications (column "יישומים": access permission only; add/update/delete bits are set in MANTI 2's
permissions screen). Nothing is removed or changed, and what they already have is left as it is (every
INSERT is `WHERE NOT EXISTS`). Tick or untick "שמירה" per row as for new users. A user code can be 3 digits
or more.

## Choosing the database server
The page shows a "שרת:" list above the run button. The default is `SQL25DEV`;
the other is `SQL2025CLN2.iscar.com,2433` (same database `Manti_IscarData`). The list is `Agent:Servers` in
`appsettings.Development.json`, and the SQL hook `.claude/hooks/guard-sql.ps1` allows the same two: to add a server,
change both. The browser sends only a name from the list, never an address.
- The agent itself only reads (SELECT) on both servers.
- **"אישור ושמירה" works on a server that is allowed to write:** `SQL25DEV` (Windows) and `SQL2025CLN2`
  (SQL login, `"AllowWrite": true` in `Agent:Servers`, approved 2026-11-08). The script runs on the server the request
  was read from (`server.txt` in the request folder). A server without `AllowWrite` is shown as "(קריאה בלבד)"
  and the backend refuses to run its script. Use a SQL login with INSERT/UPDATE on the four tables for `SQL2025CLN2`.
- Interactive Claude Code sessions have both as MCP servers: `manti-sql` and `manti-sql-cln2` (`.mcp.json`).
- The Outlook button always uses the default server.
- Not verified on `SQL2025CLN2`: the HR lookup reads through a linked server (`SQLCLN1`) that may not exist there.

### SQL2025CLN2 uses a SQL login (username + password)
Set it once, **in your own PowerShell** (never in a file, a command line or the chat):
```
.\scripts\set-cln2-login.ps1
```
It asks for the login name and the password (hidden), stores them in the API project's **user-secrets**
(`Agent:ServerLogins:SQL2025CLN2:User` / `:Password`) and, if you agree, in your Windows user environment
variables `MANTI_SQL_CLN2_USER` / `MANTI_SQL_CLN2_PASSWORD` (used only by the MCP server). Then
`.\scripts\dev.ps1 restart`. Until it is set, the server is in the list but cannot be chosen.
Use a login that can only read (`db_datareader`). How it is used: the website connects with it for the lookups;
the agent's `sqlcmd` gets the password only as the environment variable `SQLCMDPASSWORD` of its own process (never
`-P`), and the hook blocks reading or printing environment variables.

## Use (the person in charge of permissions)
1. Paste the email text, or upload the certification PDF (from the training system, saved as a file).
2. Press **הפעל סוכן** and wait 1–3 minutes.
3. Read the plan, especially the open questions. An open question means nothing was scripted for
   that item.
4. Copy the script, open SSMS on `SQL25DEV`, run it whole (no `GO`), check the result rows,
   then `ROLLBACK TRAN;`. If it looks right, run it again and `COMMIT TRAN;`.
5. Check in MANTI2 (UserProfile screen) that the permissions appear.

Every run leaves its files in `.agent-output\<yyyyMMdd-HHmm>\` (plan, script, query results).
This folder holds employee data and is not in git.

## Limits today
- Test server only; production needs a read-only SQL account first.
- No login: the page runs only in the Development environment, on the local machine.
- One run at a time; a second request waits for the first.
