# סיכום למפתחים — סוכן הרשאות (spec 002)

**מצב ל-28.10.2026.** הסוכן עובד מקצה לקצה על שרת הטסט `SQL16DEV`: מייל או טופס הסמכה PDF ← דף ב-MANTI3 ← סוכן Claude Code שבודק מול ה-DB בקריאה בלבד ← תוכנית וסקריפט SQL ← אדם מריץ ב-SSMS. נבדק עד כניסה ל-MANTI2 עם יוזר שנוצר. **שום דבר עדיין לא ב-git.**

מסמכים קשורים: [spec](spec.md) · [תוכנית עבודה](team-plan.md) · [מודל ההרשאות של MANTI2](../../knowledge/permissions-schema.md) · [הפעלה ושימוש](../../docs/how-to/access-requests.md) · [מצגת](../../docs/presentation/access-agent.html) · [תסריט הדגמה](../../docs/presentation/demo-script.md) · [שאלות פתוחות לעסק](https://claude.ai/artifact/3Wt14Ju9ZyJr3yVVuYTYt1)

---

## 1. איך זה בנוי

```
דפדפן  /access-requests  (Blazor, Pages/AccessRequests.razor)
   │  POST /api/access-requests   { emailText | pdfBase64 }        ← Development בלבד
   ▼
API  ProcessAccessRequestEndpoint
   ▼
Application  ProcessAccessRequestHandler  (+ Validator: טקסט XOR PDF, ≤20K תווים / ≤5MB, %PDF)
   │  IAccessRequestAgent  (port)
   ▼
Infrastructure  ClaudeCliAgent
   │  שומר קלט ב-.agent-output/inputs/web-<time>.md|pdf
   │  מריץ: claude.cmd -p "<prompt>" --permission-mode acceptEdits
   │        --allowedTools Read,Write,Edit,Glob,Grep,Agent,Task,Bash(sqlcmd *),Bash(pdftotext *),Bash(date *),Bash(ls *)
   │        --disallowedTools PowerShell      (cwd = C:\_1_MANTI3, timeout 10 דק', ריצה אחת בכל פעם)
   ▼
Claude Code  →  subagent access-request-agent  →  skill process-access-request
   │  sqlcmd -S SQL16DEV ... -Q "SELECT ..."   ← עובר דרך hook guard-sql.ps1
   │  pdftotext -raw / רגיל  (לטופס)
   ▼
.agent-output/<yyyyMMdd-HHmm>/  plan.md · script.sql · q1..qN.txt (ראיות) · form*.txt
   ▼
ה-API קורא plan.md + script.sql ומחזיר לדף  →  אדם מעתיק ל-SSMS: F5, ROLLBACK, ואז COMMIT
```

הסוכן **אף פעם לא כותב ל-DB**. הוא כותב קובץ, ואדם מריץ.

## 2. מפת קבצים

| נתיב | מה זה |
|---|---|
| `CLAUDE.md` | מפנה ל-`AGENTS.md` + 5 כללים קשיחים |
| `.claude/rules/db-readonly.md`, `pii-and-logging.md`, `access-requests-scope.md` | כללים (מנחים, לא אוכפים) |
| `.claude/hooks/guard-sql.ps1` + `.claude/settings.json` | **אכיפה**: SELECT בודד בלבד, רק `-S SQL16DEV`, בלי `-i`/`-P`/סודות |
| `.claude/agents/access-request-agent.md` | ה-subagent: כלים, מודל sonnet, פלט רק ל-`.agent-output` |
| `.claude/skills/process-access-request/SKILL.md` | התהליך: פענוח → אימות → תוכנית → סקריפט |
| `…/references/parsing.md` | חוקי פענוח: מייל, טופס PDF (תוויות הפוכות), היפוך RTL, "דוגמא" |
| `…/references/script-templates.md` | תבניות SQL מאומתות |
| `…/references/new-user-defaults.md` | **הקובץ לשינוי כללים**: קבוצה, מרכזים לפי מחלקה (OFF), מסך 1712, מוסמך |
| `knowledge/permissions-schema.md` | כל מה שלמדנו על הסכמה וה-SPs של MANTI2 |
| `src/…/Application/Features/AccessRequests/ProcessRequest/` | Command, Validator, Handler, port `IAccessRequestAgent` |
| `src/…/Infrastructure/Agent/ClaudeCliAgent.cs` | מפעיל את `claude -p` וקורא את הפלט |
| `src/…/Api/Endpoints/AccessRequests/ProcessAccessRequestEndpoint.cs` | ה-endpoint (רק ב-Development) |
| `src/…/Api/appsettings.Development.json` | סעיף `Agent`: WorkingDirectory, Command, TimeoutMinutes |
| `src/Manti3.Frontend/Pages/AccessRequests.razor` + `.razor.css` | הדף. Markdig (חדש ב-`Directory.Packages.props`) עם `DisableHtml` |
| `src/Manti3.Frontend/Program.cs` | timeout של HttpClient הועלה ל-15 דק' |
| `tests/Manti3.Backend.UnitTests/ProcessAccessRequestTests.cs` | 10 בדיקות (נכתבו לפני הקוד) |
| `tests/agent-evals/` | מקרה בדיקה לסוכן עם נתונים בדויים |
| `.gitignore` | `.agent-output/` (נתוני עובדים) |
| `.agent-output/` (לא ב-git, לא מועבר) | קלטים, תיקייה לכל ריצה, סקריפט איפוס לעובד הבדיקה. נתונים אמיתיים — נשאר אצל מי שהריץ |

## 3. כללים עסקיים שנקבעו (28.10.2026)

| נושא | הכלל |
|---|---|
| DB | קריאה בלבד לסוכן. כרגע רק `SQL16DEV` |
| פסילה | מתעלמים לגמרי (גורם אחר) |
| מרכז עבודה | רק מה שכתוב בבקשה. לפי מחלקה — בנוי אבל **כבוי** |
| העתקה מ"דוגמא" | רק מסכים שחסרים לעובד. **לא** מעתיקים מרכזי עבודה של הדוגמא. לא דורסים מסך קיים |
| יוזר חדש | מה-HR (`PRS_WORKERS_V6`): שם וסיסמה = מספר עובד, קבוצה 196 (מתלמדים) |
| מסך 1712 ליוזר חדש | אם **יותר ממחצית** הפעילים במחלקה מחזיקים בו; ביטים = הנפוצים בין המתלמדים במחלקה, אחרת `100000000111111111011111111111` |
| טופס הסמכה | רק אם "אישור הסמכה: מאשר" + "בוצע בהצלחה". מוסמך = לא מתלמד: הקבוצה הנפוצה במחלקה (בלי 0/196), `IsCertified=1`, תאריך המבחן |
| עמימות | שאלה, לא ניחוש. **קוד הפוך מעברית** (`S1N`↔`N1S`) נפתר מול ה-DB, לא שאלה |
| כל הרשאה שהתבקשה | נכנסת לסקריפט גם אם קיימת — `NOT EXISTS` בזמן ריצה משחזר הרשאה שנעלמה |

## 4. מלכודות ב-MANTI2 שגילינו (חשוב!)

| מה | למה זה משנה |
|---|---|
| `uspAuthorization_WorkCtrs_IUD` הוא **מתג** (קיים → מוחק) | לעולם לא לקרוא לו. INSERT ישיר עם `NOT EXISTS` |
| `uspAuthorization_IUD 'U'` = upsert לפי (UserId, ObjId) | דורס ביטים. מוסיפים רק מסכים חסרים |
| טריגר `Authorizations_Ins_Upd` מחתים **שורה אחת** לפקודה | פקודת INSERT נפרדת לכל מסך |
| טריגר `Authorizations_D` קורא UserId כסקלר | DELETE של מסכים — שורה אחת לפקודה |
| `User_Profiles_D` **שבור ב-SQL16DEV** (Msg 213 — טבלת הארכיון לא תואמת) | אי אפשר למחוק יוזר בטסט. לאיפוס: מוחקים מסכים אחד-אחד ומרכזי עבודה, ומסמנים את היוזר לא פעיל ומשנים לו `UserCode`/`UserName` ל-`<מספר>-old-<זמן>` |
| `UserCode` הוא varchar | תמיד `'12345'` במירכאות |
| `UsersStatus='1'` = לא פעיל (2,888 יוזרים) | עובד לא פעיל = שאלה |
| עמודות קצרות: שם 15, טלפון 8, סיסמה 10 | חריגה = שאלה |
| `UserFromVB` מחזיר 0 מ-SSMS | `UserLastModified=0`, התאריך כן נרשם |
| `uspGet_Authorization_WorkCtrs` מציג רק `WorkCtrDisplay=1` | מרכז מוסתר = לא תקין |
| מייל בעברית הופך קודים לטיניים בהעתקה | הסוכן בודק קוד + היפוך מול ה-DB |
| `pdftotext -layout` מפרק את הטבלה | `-raw` לשדות (עברית יוצאת הפוכה), רגיל לכותרת |
| אין לנו גישה ל-`Manti_IscarDeleted` | לא ניתן לתקן את טבלת הארכיון בעצמנו |

## 5. שכבות בטיחות

1. חשבון SQL — **כרגע אישי ועם הרשאות כתיבה** (חסר: חשבון קריאה בלבד).
2. Hook `guard-sql.ps1` — חוסם כתיבה, קבצי קלט, סיסמאות וכל שרת מלבד `SQL16DEV` (15/15 בבדיקות; נבדק גם בסשן אמיתי).
3. ה-subagent: כלים מוגבלים, PowerShell חסום, Bash רק ל-`sqlcmd`/`pdftotext`/`date`/`ls`.
4. הסקריפט: טרנזקציה, COMMIT בהערה, `NOT EXISTS` בכל פקודה.
5. ראיות: כל שאילתה ותוצאתה נשמרות (`q*.txt`) ומופיעות ב-`plan.md`.
6. Endpoint רק ב-Development; לוגים בלי תוכן (רק שמות ואורכים); Markdown בדף בלי HTML ובלי קישורים שאינם http(s).

## 6. מה נבדק

| בדיקה | תוצאה |
|---|---|
| Hook — 15 מקרים (SELECT, INSERT, SELECT INTO, `-i`, `-P`, סודות, שרתים) | ✅ 15/15 |
| בדיקות יחידה (כולל ארכיטקטורה) | ✅ 21/21 |
| מייל אמיתי לעובדים קיימים → SSMS | ✅ ההרשאות במקום |
| יוזר חדש (עובדת בדיקה): דף → סוכן → SSMS → כניסה ל-MANTI2 | ✅ |
| הדף דרך ה-API: מייל (72 שנ') ו-PDF (80 שנ') | ✅ |
| טופס הסמכה (עובדת חדשה) — סקריפט | ✅ נוצר, ⏳ לא הורץ ב-SSMS |
| עובד **קיים** שהוסמך (ה-UPDATE היחיד) | ❌ לא נבדק |
| טופס שלא אושר | ❌ לא נבדק |
| עובד חדש במחלקת ייצור בלי "דוגמא" (ברירת מחדל 1712 עם ביטי מתלמדים) | ❌ לא נבדק |
| הדף החדש בעיצוב — לחיצה בדפדפן | ⚠️ נבנה ונפתח, לא נוסה בלחיצה |

## 7. חוב טכני ופתוחים

**חוסם לפרוד**
- חשבון SQL לקריאה בלבד (היום: חשבון אישי עם db_owner; ה-hook הוא ההגנה היחידה).
- הזדהות לדף + שרת; היום רץ רק על מחשב מפתח.
- תשובות לעסק — [37 שאלות](https://claude.ai/artifact/3Wt14Ju9ZyJr3yVVuYTYt1), ובפרט: אתרים נוספים (ZURIM), מי מריץ בפרוד, מידע אישי ל-LLM.

**קוד**
- **אין git** — הפרויקט הועבר כעותק של תיקייה. הצעד הראשון: `git init` ו-repo ב-GitHub של החברה.
- `GlobalExceptionHandler`: JSON לא תקין → 500 במקום 400.
- אין בדיקת אינטגרציה ל-`ClaudeCliAgent` ול-endpoint (תלוי ב-Claude Code וב-DB).
- הסוכן לא דטרמיניסטי: אותו קלט יכול להחזיר ניסוח שונה; הסיכום חוזר באנגלית.
- `AGENTS.md` עדיין דורש פריט ניווט לכל דף; הקונסטיטוציה ביטלה את זה — לסגור.

**סביבה**
- `User_Profiles_D` ב-`SQL16DEV` — לדווח ל-DBA.
- `Agent:WorkingDirectory` ב-`appsettings.Development.json` מצביע על `C:\_1_MANTI3` — לשנות אם הפרויקט במקום אחר.

## 8. הצעדים הבאים

1. לבקש חשבון SQL לקריאה בלבד, ולהחליף בו את החשבון האישי.
2. commit על ענף (`feat/002-access-requests`).
3. להריץ את טופס ההסמכה ב-SSMS, ולמצוא דוגמאות לשלושת המקרים שלא נבדקו.
4. להעביר את מסמך השאלות לאחראית ולמנהלים, ולעדכן את `new-user-defaults.md` ו-`parsing.md` לפי התשובות.
5. שרת + הזדהות לדף, ואז פרוד.
6. מהסדנה: ביקורת adversarial על ה-skill והסקריפטים, Playwright לדף, ו-MSSQL MCP לגילוי סכמה.
