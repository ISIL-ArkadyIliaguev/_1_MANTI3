# Defaults for a new user (owner: the team; edit this file to change them)

Applied **only to a worker whose MANTI user is created by this script**. Existing users get only
what the email asks for. Decided 2026-10-28 from the data of the 52 active trainees on SQL16DEV (the agent now runs on
SQL25DEV: 50 trainees on 2026-11-04, same pattern for screen 1712)
(analysis in `knowledge/permissions-schema.md`, section 5).

## User
| Setting | Value |
|---|---|
| User name | English first name + first letter of the English last name (`Doron` + `Shapira` = `DoronS`; changed 2026-11-08: was 2 letters, and before 2026-11-05 the employee number); one more letter of the last name while the name is taken. English names from HR `FirstNameE` / `LastNameE`; if missing, a translation of the Hebrew name. Must be free (`UserName` is unique): the website resolves a taken name by adding letters, it is not a question |
| EngFirstName / EngLastName | the same English names, first letter capital and the rest lowercase (`Feras`, `Abdalla`), up to 15 |
| Password | employee number |
| User group | 196 (מתלמדים) |
| Department | from HR (`PRS_WORKERS_V6`, mapped to a MANTI department) |

## Certified worker (from an approved certification form)
Not a trainee: the group is the **most common user group among active users of the worker's
department, excluding 196 (מתלמדים) and 0 (ללא קבוצה)**. No such users, or a tie for first
place: a question, nothing scripted for that worker. Example (2026-10-28): department 31086
"ביקורת גמר-שלב ג" → 146 "מתקש שלב ג" (5 of 6).

```sql
SELECT TOP 2 u.UserGroupId, g.UserGroupName, COUNT(*) AS Users
FROM dbo.User_Profiles u LEFT JOIN dbo.User_Groups g ON g.UserGroupId = u.UserGroupId
WHERE u.DepartmentId = <DepartmentId> AND u.UsersStatus <> '1' AND u.UserGroupId NOT IN (0, 196)
GROUP BY u.UserGroupId, g.UserGroupName ORDER BY COUNT(*) DESC;
```
(`TOP 2` shows a tie: two rows with the same count.) Certification: `IsCertified = 1`,
`CertificationDate` = test date, `CertificationDateEnd` left empty (set for only 15 of 771).

## Work centers: by department

**Switch: OFF** (2026-10-28: for now only the work center named in the email is granted; the team
will turn this on later). While OFF, skip this section entirely: do not query or list the
department's work centers. Work centers are never copied from the example user.

When ON: every work center with `WorkCtrDisplay = 1` under the worker's department, **plus** any
work center named in the email. If the department has no displayed work centers, write
"department <id> has no work centers" in the plan and grant only the ones named in the email.

```sql
SELECT WorkCtrId, WorkCtrCode, WorkCtrName FROM dbo.Work_Centers
WHERE DepartmentID = <DepartmentId> AND WorkCtrDisplay = 1 ORDER BY WorkCtrCode;
```

## Default screens: by department majority
A screen in this table is given when **more than half of the active users in the worker's
department** have it (exactly half is not a majority). With fewer than 3 active users in the
department, use the fallback.

The bits are the most common `ControlId` for that screen among **active trainees (group 196) in
the department**; if there are none, the fallback bits. Never take the bits from all department
users: managers carry broader bits (in department 30753 the most common 1712 bits include
app-specific flags trainees do not have).

| ObjId | Screen | Fallback (fewer than 3 peers) | Fallback bits |
|---|---|---|---|
| 1712 | spc/דיווח | give | `100000000111111111011111111111` |

To add a default screen, add a row. Keep the rule the same for every row.

```sql
-- Peers in the department that have the screen
SELECT COUNT(*) AS Peers, COUNT(DISTINCT a.UserId) AS WithScreen
FROM dbo.User_Profiles u
LEFT JOIN dbo.Authorizations a ON a.UserId = u.UserId AND a.ObjId = <ObjId>
WHERE u.DepartmentId = <DepartmentId> AND u.UsersStatus <> '1';

-- Most common bits for the screen among trainees of the department (empty = use fallback bits)
SELECT TOP 1 a.ControlId, COUNT(*) AS Users
FROM dbo.Authorizations a JOIN dbo.User_Profiles u ON u.UserId = a.UserId
WHERE a.ObjId = <ObjId> AND u.DepartmentId = <DepartmentId> AND u.UsersStatus <> '1'
  AND u.UserGroupId = 196
GROUP BY a.ControlId ORDER BY COUNT(*) DESC;
```

## Lobby toolbar (added 2026-11-05)
Every new user also gets one row in `dbo.User_Toolbars`: the toolbar named **Lobby** whose first
button is application **742**. The website adds it by itself (`AccessScriptBuilder`); the agent
writes nothing for it. Columns written: `UserId`, `BranchId` (the worker's branch),
`USER_TBCODE = 1` (the first toolbar), `USER_TBTEXT = 'Lobby'`, `USER_B1 = '1'`,
`USER_OPERCODE1 = 742`; the other buttons keep the column defaults (`USER_B2..6 = '1'`,
`USER_OPERCODE2..6 = 0`). In `User_Toolbars` the `USER_OPERCODE1..6` columns hold the application
(ObjId) of each button and `USER_TBTEXT` is the toolbar's caption. No trigger on the table.
To change the toolbar number, the caption or the application, edit the three constants at the top
of `AccessScriptBuilder.cs`.

## Precedence
If the email names an example user who has the same screen, **the example's version wins**:
write the example's copy statements **before** the default-screen statements, so `NOT EXISTS`
keeps the example's row. In the plan, show for each screen where it came from
(example / department default).
