# מתחילים כאן — MANTI3 / סוכן הרשאות

ברוך הבא. זה מסמך הפתיחה לפרויקט. קרא אותו, ואז המשך עם Claude Code — כל ההקשר שהוא צריך נמצא בתיקייה.

## מה הפרויקט
MANTI3 הוא פרויקט חדש (Blazor WASM + ASP.NET Core Web API, .NET 10) שמחליף בהדרגה את MANTI2.
הפיצ'ר הראשון שנבנה בו הוא **סוכן הרשאות**: מקבל מייל בקשת הרשאות או טופס הסמכה (PDF), בודק מול
ה-DB של MANTI **בקריאה בלבד**, ומכין תוכנית וסקריפט SQL שאדם מריץ ב-SSMS. עובד מקצה לקצה על שרת
הטסט `SQL25DEV`; עוד לא בפרוד.

## 1. התקנה (חד-פעמי)
1. שים את התיקייה ב-`C:\_1_MANTI3`.
   (במקום אחר? שנה `Agent:WorkingDirectory` ב-`src\Manti3.Backend\Manti3.Backend.Api\appsettings.Development.json`.)
2. התקן: .NET 10 SDK, [Claude Code](https://claude.com/claude-code) (מחובר לחשבון שלך), Git for Windows
   (ממנו מגיע `pdftotext`), ו-`sqlcmd` (SQL Server command-line tools).
3. ודא גישה: Windows auth ל-`SQL25DEV` / `Manti_IscarData`. עדיף חשבון **לקריאה בלבד** (ראה "מה חסר").
4. בדיקה שהכל עובד:
   ```
   cd C:\_1_MANTI3
   dotnet build MANTI3.slnx
   dotnet test tests\Manti3.Backend.UnitTests
   ```
   מצופה: Build succeeded, ו-21/21 בדיקות עוברות.
5. צור git (הפרויקט הועבר בלי היסטוריה):
   ```
   git init
   git add .
   git commit -m "MANTI3 + access-requests agent (handover)"
   ```
   ובהמשך repo ב-GitHub של החברה. `.gitignore` כבר מוציא את `.agent-output/` (נתוני עובדים).

## 2. להמשיך עם Claude Code
פתח טרמינל (או VS Code) ב-`C:\_1_MANTI3` והרץ `claude`. Claude Code טוען לבד את `CLAUDE.md`,
`AGENTS.md`, הכללים ב-`.claude/rules/`, ה-hook שחוסם כתיבה ל-DB, ה-skill וה-subagent של הסוכן.

**הפרומפט הראשון שכדאי לתת לו (העתק-הדבק):**
```
קרא את START-HERE.md ואת specs/002-access-requests/dev-summary.md, ואז את spec.md ו-knowledge/permissions-schema.md.
סכם לי במשפטים קצרים: מה בנוי, מה נבדק, מה פתוח, ומה הצעד הבא המומלץ. אל תשנה קבצים עדיין.
```

**להריץ את הסוכן ידנית** (בתוך `claude`, בתיקיית הפרויקט):
```
@access-request-agent process .agent-output/inputs/<קובץ המייל או ה-PDF>
```
הפלט נכתב ל-`.agent-output\<תאריך-שעה>\` (`plan.md`, `script.sql`, וקבצי ראיות).

**להריץ את הדף** (שני טרמינלים):
```
dotnet run --project src\Manti3.Backend\Manti3.Backend.Api --launch-profile https
dotnet run --project src\Manti3.Frontend --launch-profile https
```
ואז `https://localhost:7296/access-requests`.

## 3. כללי ברזל (חובה)
1. **ה-DB לקריאה בלבד.** הסוכן מריץ רק SELECT, ורק מול `SQL25DEV`. הסקריפט נכתב לקובץ — **אדם מריץ**.
2. **לא לקרוא ל-SPs של ההרשאות ב-MANTI2** — חלקם מתגים או דורסים. הפרטים ב-`knowledge/permissions-schema.md`.
3. **שורה אחת לפקודה** ב-`Authorizations` (טריגרים שמטפלים בשורה אחת).
4. **פסילה — מתעלמים.** גורם אחר אחראי עליה.
5. **בלי נתוני עובדים אמיתיים** ב-git, בלוגים או בבדיקות. `.agent-output/` נשאר מקומי.
6. **עמימות = שאלה, לא ניחוש.** (קוד שהתהפך בעברית, `S1N`↔`N1S`, נפתר מול ה-DB.)

## 4. סדר קריאה
| # | קובץ | למה |
|---|---|---|
| 1 | `specs/002-access-requests/dev-summary.md` | **המפה**: ארכיטקטורה, קבצים, כללים, מלכודות, מה נבדק, חוב טכני |
| 2 | `specs/002-access-requests/spec.md` | הדרישות (FR-001..017) |
| 3 | `knowledge/permissions-schema.md` | הטבלאות, ה-SPs והטריגרים של MANTI2 — מה שגילינו |
| 4 | `.claude/skills/process-access-request/` | איך הסוכן עובד; `references/new-user-defaults.md` = הכללים העסקיים לשינוי |
| 5 | `docs/how-to/access-requests.md` | הפעלה ושימוש |
| 6 | `AGENTS.md`, `CLAUDE.md`, `.specify/memory/constitution.md` | חוקי הפרויקט והמתודולוגיה (Spec Kit + OKF) |
| 7 | `docs/presentation/access-agent.html` | מצגת של 5 שקפים (נפתחת בדפדפן) |

## 5. מה חסר / מה לבקש
- **חשבון SQL לקריאה בלבד** — החוסם המרכזי לפרוד. היום עבדנו עם חשבון אישי שיכול לכתוב; ה-hook היה ההגנה היחידה.
- **מסמך 37 השאלות לעסק** — מסמך Claude פרטי. בקש ממי שהעביר לך את התיקייה לשתף אותו איתך.
- **דוגמאות לבדיקה** — מייל/טופס אמיתיים (נשמרים רק ב-`.agent-output\inputs\`, לא ב-git).

## 6. הצעדים הבאים (לפי סדר)
1. git + repo.
2. חשבון קריאה בלבד, ולהחליף בו את החשבון האישי.
3. לבדוק שלושה מקרים שלא נבדקו: עובד **קיים** שהוסמך (ה-UPDATE היחיד), טופס שלא אושר, ועובד חדש בלי "דוגמא".
4. לעדכן כללים לפי תשובות העסק (`new-user-defaults.md`, `parsing.md`).
5. שרת + הזדהות לדף, ואז פרוד.
6. מהסדנה: ביקורת adversarial, Playwright לדף, MSSQL MCP.
