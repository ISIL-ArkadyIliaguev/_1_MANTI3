# Outlook add-in: a button that opens MANTI3 in a popup

A ribbon button **"MANTI3 · הרשאות"** appears on a mail in Outlook. Pressing it:

1. takes **the mail that is selected/open** (as an `.eml` file, or its text if Outlook cannot give the file),
2. sends it to the backend, which **starts the agent in the background** (`POST /api/access-requests/jobs`),
3. opens a popup on that request (`/access-requests?job=<id>`), which shows the progress, and when the agent
   finishes, the explanation, the users table and the **"אישור ושמירה"** button (it updates the script, dry-runs it and saves). There is no step of adding files.

Outside Outlook the same page still works as before: drag mails / PDFs, or paste text.

The add-in is three static files served by the frontend itself, in
`src/Manti3.Frontend/wwwroot/outlook/`:

| File | What it is |
|---|---|
| `manifest.xml` | The add-in description Outlook installs (name, icons, the button, the page it loads) |
| `commands.html` + `commands.js` | The invisible page Outlook loads; `openManti()` opens the popup with `Office.context.ui.displayDialogAsync` |
| `icon-*.png` | Button icons |

## Run (development)
The popup must be **HTTPS**, so the frontend runs on its https profile:
```
.\scripts\dev.ps1 restart
```
(starts the API on **https://localhost:7127** and the frontend on **https://localhost:7296**.) The development
certificate must be trusted: `dotnet dev-certs https --trust` (already done on this machine).
Check in a browser: `https://localhost:7296/outlook/manifest.xml` opens without a certificate warning.

## Install in classic Outlook for Windows (once per person)
Checked on this machine: classic Outlook 16.0.20527 (Microsoft 365 Apps), Microsoft 365 sign-in, and the
**WebView2 runtime** (156) is installed. The popup runs in WebView2, which the Blazor page needs; an Outlook
that still uses the old Internet Explorer engine cannot show it.

1. Start the servers: `.\scripts\dev.ps1 restart` (the frontend must be on https://localhost:7296).
2. In classic Outlook: **Home → Get Add-ins** (תוספות) → **My add-ins** → under **Custom Addins**:
   **Add a custom add-in → Add from File…** → choose
   `src\Manti3.Frontend\wwwroot\outlook\manifest.xml` → accept the warning.
   (Alternative: **File → Manage Add-ins** opens the same "Add-ins for Outlook" page in the browser.)
3. Select a mail that mentions an authorization or a work center (see below). The button
   **"MANTI3 · הרשאות"** is in the group **MANTI3** on the **Home** tab (on a mail opened in its own
   window: the **Message** tab). Under "…" if the ribbon is narrow.
4. After a change to `manifest.xml` (the version is in `<Version>`): remove the add-in, **close Outlook**,
   delete the contents of `%LOCALAPPDATA%\Microsoft\Office\16.0\Wef\` (the add-in cache), open Outlook, and
   add it again.

**If the popup is blank or the button does nothing:** open `https://localhost:7296/access-requests` in
Edge first (certificate and servers OK?), then check **File → Office Account → About Outlook** shows a
current build, and that the WebView2 runtime is installed (Settings → Apps).

## Which mails get the button
**Every mail.** The button is always on the ribbon (since 1.2.0.0); there is no word filter. (Earlier versions
showed it only on mails with words like "authorization" or "work center"; that rule was removed from `manifest.xml`.)
After updating the manifest: **remove the add-in in Outlook and add it again** (Outlook caches the manifest).

## Side panel (since 1.3.0.0), not a popup
Outlook asks "wants to display a new window" (Allow / Ignore) before every **popup**, and in the new Outlook the
question cannot be switched off (`promptBeforeOpen: false` makes the browser block it, error 12011). So the button
now opens a **side panel** on the right (`outlook/taskpane.html`): it sends the open mail to the backend, then moves
to the access-requests page of that run. No question. The panel is narrower than the old popup and its width is
Outlook's. `commands.html` / `commands.js` (the popup version) are still served but the manifest no longer uses them.
To go back to the popup: in `manifest.xml` replace the `ShowTaskpane` action with the `ExecuteFunction` `openManti`.

## A mark on the mail row itself
Outlook does **not** let an add-in put a button (or anything) on a row of the message list; add-ins live on
the ribbon and in the reading pane only. The closest thing is a coloured **category** on the row, set by an
Outlook rule, so matching mails stand out and can be sorted/filtered:

1. Outlook (classic): **Home → Rules → Manage Rules & Alerts → New Rule… → Apply rule on messages I receive**.
2. Condition: **with specific words in the subject or body**, and enter: `authorization`, `work center`,
   `הרשאה`, `הרשאות`, `מרכז עבודה`.
3. Action: **assign it to the category** → create a category "MANTI3 הרשאות" with a colour.
4. Finish, and **Run Rule Now** to tag the mails already in the Inbox.

The coloured tag then shows on each matching row. Select that mail and press the add-in button on the ribbon.
(The tag and the button use separate word lists: keep them the same by hand.)

## Limits to know
- **The frontend must be running** when the button is pressed, on `https://localhost:7296`. If it is not,
  Outlook shows an error notification on the mail.
- **A Microsoft 365 / Exchange mailbox is needed** to install a custom add-in from a file. A tenant
  administrator can block custom add-ins; then IT must deploy it (Admin Center → Integrated apps).
- **`localhost` works only on the same machine.** For other people, host the frontend (and the API) on a
  real HTTPS address, replace every `https://localhost:7296` in `manifest.xml` with it, and add that address
  to the API's `Cors:AllowedOrigins`.
- **Only the mail body is read.** A PDF form attached to the mail is not passed to the agent yet; for a
  certification form use the website (drag the PDF in).
- **The agent runs on the server, in the background.** Closing the popup does not stop it, and the result is kept
  in memory for 2 hours (and lost if the API restarts: then press the button again).
- Not verified inside a real Outlook yet (see the project notes): the files are served and the manifest is
  well-formed, but the popup itself has to be tried in Outlook.
