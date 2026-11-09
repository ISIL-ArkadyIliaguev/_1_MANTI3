---
type: reference
title: MANTI 2 permissions model (users, work centers, screens)
updated: 2026-10-28
status: mostly verified - procedure bodies and columns read in SSMS on 2026-10-28; insert trigger and UsersStatus values still to check
---

# MANTI 2 permissions model

Used by the `process-access-request` skill. Sources: MANTI 2 VB.NET code
(`C:\_1_MANTI2\MANTI2Source`) and the bodies of the stored procedures, read in SSMS.
**VERIFIED** = seen in a procedure body. **VERIFY** = still an assumption.

> The agent may run only `SELECT` (the hook blocks `EXEC`), so it reads these tables directly and
> never calls the procedures.

## Tables (all `dbo`, database `Manti_IscarData`)

| Table | Columns used | Status |
|---|---|---|
| `User_Profiles` | `UserId`, `DepartmentId`, `UserFirstName`, `UserLastName`, `UsersStatus` (varchar, default `''`); employee number = **`UserCode` varchar NOT NULL** | VERIFIED |
| `Departments` | `DepartmentID`, `FactoryID`, `DepartmentName`, `DepartmentCode`, `DepartmentDisplay` | VERIFIED |
| `Factories` | `FactoryId`, `FactoryName`, `FactoryCode` (e.g. `H2`), **`BranchId` int NOT NULL**, `FactoryDisplay` | VERIFIED |
| `Work_Centers` | `WorkCtrId`, `WorkCtrCode` (e.g. `S1N`), `WorkCtrName`, `DepartmentID`, `WorkCtrDisplay` | VERIFIED |
| `Authorisations_WorkCtrs` | **only** `UserId` int, `WorkCtrId` int, both NOT NULL, no defaults (note the spelling *Authorisations*). Trigger: `Authorisations_WorkCtrs_D` (on delete only) | VERIFIED |
| `Authorizations` | `UserId`, `BranchId`, `ObjId` (int NOT NULL), `ControlId char(30)` NOT NULL default `'100001111111111111001111111111'`, `DateLastModified`, `UserLastModified` (nullable, no default). Triggers: **`Authorizations_Ins_Upd`**, `Authorizations_D` | VERIFIED; body of `Authorizations_Ins_Upd` **VERIFY** |

**`UserCode` is a string.** Always compare with quoted literals (`UserCode IN ('10001', '10002')`),
never with numbers, or SQL Server converts every row and may fail or miss.

Inserting into `Authorisations_WorkCtrs` fires no trigger.

**`UsersStatus = '1'` means an inactive user**: MANTI 2 refuses login (`UserLogin_VM.vb:1195`) and
filters them out of user lists (`<> "1"` across the code). `''` and `'0'` are active. In the
database on 2026-10-28: `'1'` 2888 users, `''` 2187, `'0'` 1.

### Trigger `Authorizations_Ins_Upd` (FOR INSERT, UPDATE) — VERIFIED, **single-row**
```sql
EXEC UserFromVB @u OUTPUT
SELECT @a = UserId, @b = BranchId, @c = ObjId FROM inserted
UPDATE Authorizations SET userlastmodified = @u, datelastmodified = GETDATE()
WHERE UserId = @a AND BranchId = @b AND ObjId = @c
```
It reads **one** row from `inserted`. A multi-row `INSERT ... SELECT` stamps only one of the rows;
the rest keep `DateLastModified = NULL`. MANTI 2 always inserts one row per call, so every row is
stamped. **Scripts must insert one screen per statement.** `UserFromVB` decides the
`UserLastModified` value; what it returns when the script runs from SSMS is **VERIFY**.
| `Appl_Header` | `ObjID`, `AppTbToolTip` (screen name) | VERIFIED |

A user's **branch**: `User_Profiles.DepartmentId` → `Departments.FactoryID` → `Factories.BranchId`
(MANTI 2: `UserData_Srv.vb:469`).

HR source (for user creation, see section 0):
`SQLCLN1.prs_IscarData.dbo.PRS_WORKERS_V6` (`Employeeno`, `firstname`, `surname`, `DepId`, `FacId`,
`EmailAdd`, `InternalPhone`), read by `uspGet_User_Details_FromAS400` through a linked server.

## 0. Creating a user — VERIFIED 2026-10-28

- `uspUserDetails_IUD` with `'I'` is a plain `INSERT INTO User_Profiles` returning
  `SCOPE_IDENTITY()`. No other table is touched.
- Triggers on `User_Profiles`: `User_Profiles_I_U` stamps `DateLastModified` only when
  `APP_NAME()` is `Visual Basic` or `Manti` (so not from SSMS; recent users have it NULL anyway).
  `User_Profiles_D` archives deleted rows to `Manti_IscarDeleted..User_Profiles` with
  `SELECT *`. **On SQL16DEV it fails** (Msg 213: the archive table's columns no longer match), so
  a user row cannot be deleted there; to reset a test user, retire it instead (inactive, renamed).
  **SQL25DEV, checked 2026-11-04 (read-only):** same trigger body, and the archive table still has
  20 columns against 22 in `User_Profiles` (missing: `CertificationDateEnd`, `PagingPageSize`), so
  the delete fails there too. Retire, do not delete.
- Delete triggers on `Authorizations` (`Authorizations_D`) and `Authorisations_WorkCtrs` archive to
  `Manti_IscarDeleted`; `Authorizations_D` also reads `UserId` as a scalar from `deleted`, so it
  handles **one row per statement** (delete screens one at a time).
- Column sizes: `UserName` 50, `UserCode` 50, `UserPassword` **10**, `UserFirstName` /
  `UserLastName` **15**, `PhonNumber` **8**, `E_MailAdress` 50, `UsersStatus` 1, `UserBarCode` 1.
- MANTI 2 validation (`UserProfile_VM.vb:1093-1123`): user name, employee number, department and
  password are required; the user name must be unique (`CheckUniqueUserName`).
- Conventions (business decision 2026-10-28, matching the data): **user name = password =
  employee number**; new users go to group **196 = מתלמדים** (`dbo.User_Groups`). 6 of the last 8
  users created are in 196.
- New-user values in MANTI 2 (`daUser_Details.vb:583-604`): `UsersStatus = ''`, `UserBarCode = ''`,
  names with repeated spaces collapsed.

HR prefill (same as `uspGet_User_Details_FromAS400`), reachable from `SQL25DEV` (verified
2026-11-04: the linked server answers, 3129 rows in `PRS_WORKERS_V6`):
```sql
SELECT w.Employeeno, w.firstname, w.surname, w.InternalPhone, w.EmailAdd, w.DepId, w.FacId, d.DepartmentId
FROM SQLCLN1.prs_IscarData.dbo.PRS_WORKERS_V6 w
LEFT JOIN dbo.Factories  f ON w.FacId LIKE f.FactoryCode
LEFT JOIN dbo.Departments d ON w.DepId LIKE d.DepartmentCode AND d.FactoryId = f.FactoryId AND d.DepartmentDisplay = 1
WHERE w.Employeeno IN (<UserCodes>);
```

## 5. What trainees have (SQL16DEV, 2026-10-28, aggregates only)
Re-check on SQL25DEV, 2026-11-04: 50 active users in group 196, 42 of them with screen 1712
(the figures below were not re-run; the data drifted slightly). Column sizes, the five triggers
and `UsersStatus` (`'1'` 2888, `''` 2188, `'0'` 1) match section 0.
- 52 active users in group 196. Screen 1712 "spc/דיווח": 41 (79%); 877 "תוצאות ביקורת": 12;
  742: 7; everything else ≤ 4. 33 trainees have exactly one screen.
- 1712 is not trainee-specific: it is the general production reporting screen (technologists
  161/178, department managers 130/142, prototypes 67/68). Trainees without it are mostly in HB
  packing (PVD/CVD) and non-production departments (maintenance, technical office, raw-material
  inspection).
- Most common 1712 bits: `100000000111111111011111111111` (27 of 41), across factories.
- Work centers: no fixed set. 7 trainees have exactly all of their department's work centers,
  19 part of them, 12 own plus other departments, 8 only other departments, 6 none.
- Rules derived from this: `.claude/skills/process-access-request/references/new-user-defaults.md`.

## 1. Work-center authorization ("the checkmark")

- One row in `Authorisations_WorkCtrs` per (`UserId`, `WorkCtrId`) = the user is checked.
- The MANTI 2 screen lists only work centers with **`WorkCtrDisplay = 1`**
  (`uspGet_Authorization_WorkCtrs`). A hidden work center in an email is a question, not an action.
- `uspAuthorization_WorkCtrs_IUD @UserId, @WorkCtrId` is a **toggle**. VERIFIED:
  ```sql
  IF EXISTS (SELECT * FROM Authorisations_WorkCtrs WHERE UserId = @UserId AND WorkCtrId = @WorkCtrId)
      DELETE FROM Authorisations_WorkCtrs WHERE UserId = @UserId AND WorkCtrId = @WorkCtrId
  ELSE
      INSERT INTO Authorisations_WorkCtrs (UserId, WorkCtrId) VALUES (@UserId, @WorkCtrId)
  ```
  **Never call it from a script**: on a worker who is already checked it removes the permission.
  Insert directly with `WHERE NOT EXISTS`.

## 2. Screen authorization

- One row in `Authorizations` per (`UserId`, `ObjId`). `ObjId` is the screen (`Appl_Header`).
- `uspAuthorization_IUD` with `'U'` is an **upsert keyed on (`UserId`, `ObjId`) only**. `BranchId`
  was removed from the key on 14/04/21, but is still stored. VERIFIED. It does **not** set
  `DateLastModified` / `UserLastModified` (a trigger or default may; **VERIFY**).
  **Never call it from a script**: on an existing screen it overwrites the worker's bits.

### `ControlId` (char(30))
`uspGet_Authorizations` reads it with 1-based `SUBSTRING` (VERIFIED). The VB code uses 0-based
indexes, so both are shown:

| SQL position (1-based) | VB index (0-based) | Meaning |
|---|---|---|
| 2 | 1 | Delete |
| 3 | 2 | Add |
| 4 | 3 | Update |
| 5 | 4 | Copy |
| 19 | 18 | Confirm tool |
| 20 | 19 | Confirm order |
| 30 (last) | 29 | always `1` |

A space means `0`. Other positions are app-specific (`clsAppAuthorizations.AuthCharNo`).
When MANTI 2 saves a row it normalizes the string (`daAuthorizations.vb:200-248`): spaces become
`0` and the last character becomes `1`. In SQL:
`STUFF(REPLACE(CONVERT(char(30), ControlId), ' ', '0'), 30, 1, '1')`.

## 3. "Copy permissions from user" in MANTI 2

From `UserProfile_VM.vb:546-580` and `CopyWorkCntr` (`:719-724`), for an existing target user:
- **Screens:** every `ObjId` the example has and the target does **not** is added, with the
  example's `ControlId` (normalized as above) and the **target's** `BranchId`. Existing screens of
  the target are **not changed**.
- **Work centers:** every work center the example is checked for is checked for the target.
- Only adds, never removes.

Decided 2026-10-28: MANTI 3 copies **only the screens** of the example user. Work centers come
only from the email (unlike MANTI 2's copy).

## 4. Data checks before scripting
- An example user with **two rows for the same `ObjId`** (older data, from before 2021) would copy
  twice. If found, it is a question.
- An employee number with no row in `User_Profiles`: create the user from HR (section 0); any
  failed HR check is a question.
- A worker with `UsersStatus = '1'` (inactive): a question ("reactivate?"), no script. An
  inactive example user is also a question.

## Still to check in SSMS
```sql
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('User_Profiles','Factories','Authorizations','Authorisations_WorkCtrs')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

SELECT OBJECT_NAME(parent_id) AS TableName, name, is_disabled
FROM sys.triggers
WHERE parent_id IN (OBJECT_ID('dbo.Authorizations'), OBJECT_ID('dbo.Authorisations_WorkCtrs'));
```
1. ~~`User_Profiles.UserCode`~~ Done: varchar NOT NULL.
2. ~~`Factories.BranchId`~~ Done: int NOT NULL.
3. ~~Insert trigger~~ Done: single-row stamp of `DateLastModified` / `UserLastModified`.
4. ~~`UsersStatus`~~ Done: `'1'` = inactive.
5. ~~`UserFromVB` from SSMS~~ Done (2026-10-28, first committed run, a test user): it returns
   **0**, so `UserLastModified = 0` on the screen rows; `DateLastModified` is stamped on every
   row (one statement per screen works). The new `User_Profiles` row keeps both NULL.

## Related
- Spec: `specs/002-access-requests/spec.md`
- Skill: `.claude/skills/process-access-request/SKILL.md`
- Script templates: `.claude/skills/process-access-request/references/script-templates.md`
