# Script templates (owner: C)

Status: skeleton. C fills this in after A delivers `knowledge/permissions-schema.md`.

## Rules for every generated script
- INSERT statements only for items that are verified and have no open question.
- Include **every** requested item for every verified worker, also items that already exist at
  planning time. `NOT EXISTS` is evaluated when the script runs, so existing items are skipped and
  items removed in the meantime are restored.
- For unverified items: verification SELECTs only, no INSERT (not even commented out).
- Header comment with the request id, date and a line: `-- Review before running. Generated, not executed.`
- One transaction, so a human can roll back: `BEGIN TRAN; ... -- COMMIT TRAN;` (commit left for the human).
- Never include passwords or connection strings.

## Template: create a missing user (decided and verified 2026-10-28)
Only for a worker with no row in `User_Profiles` whose HR data passed every check (HR row found,
MANTI department mapped, names ≤ 15 characters, phone ≤ 8, user name not taken). Mirrors
`uspUserDetails_IUD 'I'`; never call the procedure. The password is the employee number; the
user name is the English first name + first letter of the English last name, one more letter while taken (`DoronS`, `DoronSh`; English
names from HR `FirstNameE` / `LastNameE`, also stored in `EngFirstName` / `EngLastName` as `Feras` / `Abdalla`: first letter capital, the rest lowercase);
group 196 = מתלמדים. Names are trimmed with inner spaces collapsed.

```sql
-- New user <UserCode>: <FirstName> <LastName>, department <DepartmentId>, group 196 (מתלמדים)
IF NOT EXISTS (SELECT 1 FROM dbo.User_Profiles WHERE UserCode = '<UserCode>')
    INSERT INTO dbo.User_Profiles
        (UserName, UserCode, UserPassword, UserGroupId, DepartmentId,
         UserFirstName, UserLastName, EngFirstName, EngLastName, PhonNumber, E_MailAdress, UsersStatus, UserBarCode, DateLastModified)
    VALUES
        ('<UserName>', '<UserCode>', '<UserCode>', 196, <DepartmentId>,
         N'<FirstName>', N'<LastName>', '<EngFirstName>', '<EngLastName>', '<Phone>', '<Email>', '', '', GETDATE());
DECLARE @U<UserCode> int = (SELECT UserId FROM dbo.User_Profiles WHERE UserCode = '<UserCode>');
```

**Certified worker** (approved certification form): the same INSERT with the department's group
instead of 196, plus `IsCertified, CertificationDate` in the column list and `1, '<yyyymmdd>'`
in the values (date written `yyyymmdd`, which SQL Server reads the same in every language).
An **existing** worker from an approved form instead gets:

```sql
-- <UserCode>: certified <dd/mm/yyyy> (form); leaves trainees (196) for group <GroupId>
UPDATE dbo.User_Profiles
   SET UserGroupId = CASE WHEN UserGroupId = 196 THEN <GroupId> ELSE UserGroupId END,
       IsCertified = 1,
       CertificationDate = '<yyyymmdd>',
       DateLastModified = GETDATE()
 WHERE UserCode = '<UserCode>';
```
This is the only UPDATE the agent writes. Never update any other column of `User_Profiles`. `DateLastModified = GETDATE()` is set explicitly (here and in the INSERT above): the trigger `User_Profiles_I_U` sets it only for connections from MANTI 2 (APP_NAME `Manti` or `Visual Basic`), so a script run by the website would leave it NULL. `Authorizations` needs nothing: its trigger sets `DateLastModified` on every insert.

For that worker, the work-center and screen statements below use `@U<UserCode>` instead of a
literal `UserId`, and `<WorkerBranchId>` is the branch of the HR department's factory. The whole
script is **one batch: never add `GO`**, or the variables are lost.

A new user also gets the defaults in `new-user-defaults.md`: one work-center statement per
department work center (template below), and one statement per default screen:

```sql
-- <UserCode>: default screen <ObjId> "<Name>" (department majority: <WithScreen>/<Peers>)
INSERT INTO dbo.Authorizations (UserId, BranchId, ObjId, ControlId)
SELECT @U<UserCode>, <WorkerBranchId>, <ObjId>,
       STUFF(REPLACE(CONVERT(char(30), '<ControlId>'), ' ', '0'), 30, 1, '1')
WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorizations t
                  WHERE t.UserId = @U<UserCode> AND t.ObjId = <ObjId>);
```
Order for a new user: create user → work centers → screens copied from the example → default
screens (so the example's version wins).

Verification SELECTs for a new user:
```sql
-- HR data: see knowledge/permissions-schema.md section 0
-- User name not taken by someone else
SELECT UserId, UserCode FROM dbo.User_Profiles WHERE UserName IN (<the new user names>);
-- Branch of the HR department
SELECT d.DepartmentID, f.BranchId FROM dbo.Departments d JOIN dbo.Factories f ON f.FactoryID = d.FactoryID
WHERE d.DepartmentID IN (<DepartmentIds>);
```

## Template: add a worker to a work center (VERIFIED table, 2026-10-28)
**Never call `uspAuthorization_WorkCtrs_IUD`**: it is a toggle and would remove the permission
from a worker who already has it. Insert directly, only if missing. `<UserId>` and `<WorkCtrId>`
are the integer ids found by the verification SELECTs, written as literals.

```sql
-- <UserCode> -> work center <WorkCtrCode>
INSERT INTO dbo.Authorisations_WorkCtrs (UserId, WorkCtrId)
SELECT <UserId>, <WorkCtrId>
WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorisations_WorkCtrs
                  WHERE UserId = <UserId> AND WorkCtrId = <WorkCtrId>);
```

Verification SELECT (current state of one worker):
```sql
SELECT UserId, WorkCtrId FROM dbo.Authorisations_WorkCtrs WHERE UserId = <UserId>;
```

## Verification SELECTs (run by the agent)
All columns below are verified. **`UserCode` is varchar**: write `<UserCodes>` as quoted
strings, e.g. `'10001', '10002'`, never as bare numbers. If a query fails, stop and report it.

```sql
-- Workers and example user: id, branch, active? (UsersStatus '1' = inactive)
SELECT u.UserID, u.UserCode, u.DepartmentId, u.UsersStatus, f.BranchId
FROM dbo.User_Profiles u
JOIN dbo.Departments d ON d.DepartmentID = u.DepartmentId
JOIN dbo.Factories  f ON f.FactoryID    = d.FactoryID
WHERE u.UserCode IN (<UserCodes>);

-- Work centers named in the email
SELECT WorkCtrId, WorkCtrCode, WorkCtrName, WorkCtrDisplay
FROM dbo.Work_Centers WHERE WorkCtrCode IN (<WorkCtrCodes>);

-- Screens the example has and the worker lacks (the diff for the plan)
SELECT ex.ObjId, ah.AppTbToolTip, ex.ControlId
FROM dbo.Authorizations ex
JOIN dbo.Appl_Header ah ON ah.ObjID = ex.ObjId
WHERE ex.UserId = <ExampleUserId>
  AND NOT EXISTS (SELECT 1 FROM dbo.Authorizations t WHERE t.UserId = <UserId> AND t.ObjId = ex.ObjId);

-- Duplicate screens on the example user (if any row returns: ask, do not script)
SELECT ObjId, COUNT(*) AS Rows FROM dbo.Authorizations
WHERE UserId = <ExampleUserId> GROUP BY ObjId HAVING COUNT(*) > 1;
```

## Template: copy screens from the example user
**Never call `uspAuthorization_IUD`**: on a screen the worker already has, it overwrites the bits.
MANTI 2 only adds missing screens, so the script does the same. `BranchId` is the **worker's**
branch. `ControlId` is normalized the way MANTI 2 saves it (spaces → `0`, last char → `1`).

**One statement per screen.** The insert trigger `Authorizations_Ins_Upd` stamps only one row per
statement, so a multi-row insert would leave most rows without `DateLastModified`. Generate one
statement for **every `ObjId` of the example user**, for every worker (not only the ones the diff
SELECT shows as missing; the diff is for the plan, the script re-checks at run time):

```sql
-- <UserCode>: screen <ObjId> "<AppTbToolTip>" from example <ExampleUserCode>
INSERT INTO dbo.Authorizations (UserId, BranchId, ObjId, ControlId)
SELECT <UserId>, <WorkerBranchId>, ex.ObjId,
       STUFF(REPLACE(CONVERT(char(30), ex.ControlId), ' ', '0'), 30, 1, '1')
FROM dbo.Authorizations ex
WHERE ex.UserId = <ExampleUserId> AND ex.ObjId = <ObjId>
  AND NOT EXISTS (SELECT 1 FROM dbo.Authorizations t
                  WHERE t.UserId = <UserId> AND t.ObjId = <ObjId>);
```
Each statement inserts at most one row (the duplicate check above guarantees it) and is safe to run
twice. Never collapse these into one `INSERT ... SELECT` over all screens.

## Open
- ~~Copy the example's work centers?~~ Decided 2026-10-28: **no**. Screens only; work centers
  only from the email.
- Who fills `DateLastModified` / `UserLastModified` (trigger or default). If neither, decide
  whether the script sets them.
