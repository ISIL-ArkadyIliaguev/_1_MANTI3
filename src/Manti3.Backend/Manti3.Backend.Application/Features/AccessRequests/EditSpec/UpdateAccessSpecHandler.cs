using System.Text.RegularExpressions;
using FluentValidation;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.BuildScript;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;

namespace Manti3.Backend.Application.Features.AccessRequests.EditSpec;

// מחיל את העריכות של האדם על ה-spec השמור ובונה את הסקריפט מחדש מהתבניות (AccessScriptBuilder).
// רק השדות שבטבלה ניתנים לשינוי: מי העובדים, משתמש הדוגמה, המסכים וההסמכה של קיים נשארים כפי שהסוכן קבע.
// כל מחלקה, קבוצה ומרכז עבודה חייבים להיות ברשימות של השרת שנבחר; כל שדה אחר עובר את הולידציה של הבונה
internal sealed class UpdateAccessSpecHandler(IAgentOutput output, IAccessLookups lookups)
    : ICommandHandler<UpdateAccessSpecCommand, UpdatedSpec>
{
    public async Task<Result<UpdatedSpec>> Handle(UpdateAccessSpecCommand command, CancellationToken ct)
    {
        var json = output.ReadSpec(command.RequestId);
        var spec = json is null ? null : AccessScriptBuilder.Parse(json);
        if (spec?.Workers is null)
            return Result<UpdatedSpec>.Failure(Error.NotFound(
                "AccessRequest.SpecNotFound", "There is no editable spec for this request."));

        if (output.IsCommitted(command.RequestId))
            return Result<UpdatedSpec>.Failure(Error.Conflict(
                "AccessRequest.AlreadyCommitted", "This script was already run and saved. It can no longer be edited."));

        var data = await lookups.Get(output.ReadServerName(command.RequestId), ct);
        if (!data.IsSuccess)
            return Result<UpdatedSpec>.Failure(data.Error!);

        var departments = data.Value.Departments.Select(d => d.Id).ToHashSet();
        var groups = data.Value.Groups.Select(g => g.Id).ToHashSet();
        var workCenters = data.Value.WorkCenters.ToDictionary(c => c.Id);
        var applications = (data.Value.Applications ?? []).ToDictionary(a => a.Id);

        if (command.Workers.Select(e => e.UserCode).Distinct().Count() != command.Workers.Count)
            return Invalid("A worker appears twice in the edits.");

        var workers = spec.Workers.ToList();
        foreach (var edit in command.Workers)
        {
            // עובד חדש, או עובד קיים (שמוסיפים לו רק מרכזי עבודה ויישומים)
            var index = workers.FindIndex(w => w.UserCode == edit.UserCode && (w.NewUser is not null || w.UserId is not null));
            if (index < 0)
                return Invalid("An edit is for a worker that is not in this request.");

            var worker = workers[index] with { Skip = !edit.Include };

            if (edit.NewUser is { } fields)
            {
                if (worker.NewUser is not { } current)
                    return Invalid($"Worker {edit.UserCode} already has a MANTI user: its details cannot be edited here.");
                if (!departments.Contains(fields.DepartmentId))
                    return Invalid($"Worker {edit.UserCode}: the department is not in the list.");
                if (!groups.Contains(fields.GroupId))
                    return Invalid($"Worker {edit.UserCode}: the group is not in the list.");

                worker = worker with
                {
                    NewUser = current with
                    {
                        FirstName = fields.FirstName?.Trim(),
                        LastName = fields.LastName?.Trim(),
                        EngFirstName = fields.EngFirstName?.Trim(),
                        EngLastName = fields.EngLastName?.Trim(),
                        Email = fields.Email?.Trim(),
                        DepartmentId = fields.DepartmentId,
                        GroupId = fields.GroupId,
                        // null = לא שונה (נשאר מה שהיה), ריק = חזרה לברירת המחדל (מחושב / מספר העובד), אחרת הערך שהוקלד
                        UserName = fields.UserName is null ? current.UserName
                            : string.IsNullOrWhiteSpace(fields.UserName) ? null : fields.UserName.Trim(),
                        Password = fields.Password is null ? current.Password
                            : string.IsNullOrEmpty(fields.Password) ? null : fields.Password,
                    },
                };
            }

            if (edit.WorkCenterIds is { } ids)
            {
                var distinct = ids.Distinct().ToList();
                if (distinct.Any(id => !workCenters.ContainsKey(id)))
                    return Invalid($"Worker {edit.UserCode}: a work center is not in the list.");

                worker = worker with
                {
                    WorkCenters = distinct.Select(id => new WorkCenterSpec(id, workCenters[id].Code)).ToList(),
                };
            }

            if (edit.ScreenIds is { } screenIds)
            {
                var distinct = screenIds.Distinct().ToList();
                if (distinct.Any(id => !applications.ContainsKey(id)))
                    return Invalid($"Worker {edit.UserCode}: an application is not in the list.");
                if (distinct.Count > 0 && worker.BranchId <= 0)
                    return Invalid($"Worker {edit.UserCode}: the branch of this worker is unknown, so applications cannot be added. Run the agent again.");

                // הרשאת גישה בלבד (תו 1); תו 30 נקבע בבנאי כמו בשאר המסכים. הרשאות הוספה/עדכון/מחיקה נקבעות במסך ההרשאות של MANTI 2
                var access = "1" + new string('0', 29);
                worker = worker with
                {
                    AddScreens = distinct.Select(id => new DefaultScreenSpec(id, applications[id].Caption, access)).ToList(),
                };
            }

            workers[index] = worker;
        }

        var updated = spec with { Workers = workers };
        var updatedJson = AccessScriptBuilder.ToJson(updated);
        var valid = AccessScriptBuilder.Build(updatedJson);
        if (!valid.IsSuccess)
            return Result<UpdatedSpec>.Failure(valid.Error!);

        var server = output.ReadServerName(command.RequestId);

        // שם משתמש שהאדם הקליד חייב להיות פנוי (UserName ייחודי): בודקים עכשיו, כדי שהאדם יראה את זה בטבלה ולא רק בהרצה לניסיון
        var typed = workers.Where(w => w.NewUser is { UserName: not null } && !w.Skip).Select(w => w.NewUser!.UserName!).ToList();
        if (typed.Count != typed.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return Invalid("Two new users have the same user name.");

        if (typed.Count > 0)
        {
            var taken = await lookups.TakenUserNames(server, typed, ct);
            if (!taken.IsSuccess)
                return Result<UpdatedSpec>.Failure(taken.Error!);
            if (taken.Value.Count > 0)
                return Invalid($"The user name is already taken by an existing user: {string.Join(", ", taken.Value)}. Choose another one.");
        }

        // שם מחושב (לא הוקלד): שם פרטי + אות מהמשפחה, ואם תפוס עוד אות, עד ששם פנוי
        var resolved = await SpecMapper.ResolveNames(lookups, server, updatedJson, ct);
        if (resolved.Failure is { } failure)
            return Result<UpdatedSpec>.Failure(failure);

        var withoutName = workers.Where(w => w.NewUser is { UserName: null } && !w.Skip
                                             && !resolved.Names.ContainsKey(w.UserCode!)).Select(w => w.UserCode).ToList();
        if (withoutName.Count > 0)
            return Invalid($"No free user name for worker {string.Join(", ", withoutName)}: every name made of the English first name and the last name is taken. Type a user name.");

        var script = AccessScriptBuilder.Build(updatedJson, resolved.Names);
        if (!script.IsSuccess)
            return Result<UpdatedSpec>.Failure(script.Error!);

        output.WriteSpecAndScript(command.RequestId, updatedJson, script.Value);

        var existing = await SpecMapper.ReadExisting(lookups, server, updated, ct);

        return Result<UpdatedSpec>.Success(new UpdatedSpec(
            SpecMapper.ToView(updated, existing, resolved.Names), script.Value, AccessScriptGuard.HasWrites(script.Value)));
    }

    private static Result<UpdatedSpec> Invalid(string description)
        => Result<UpdatedSpec>.Failure(Error.Validation("AccessRequest.InvalidEdit", description));
}

internal static partial class RequestIdFormat
{
    // כמו RunAccessScriptValidator: רק שם תיקייה שהסוכן יוצר
    public static bool IsValid(string? id) => !string.IsNullOrEmpty(id) && Pattern().IsMatch(id);

    [GeneratedRegex(@"^(web-)?\d{8}-\d{4,6}(-\d+)?$")]
    private static partial Regex Pattern();
}

internal sealed class UpdateAccessSpecValidator : AbstractValidator<UpdateAccessSpecCommand>
{
    public UpdateAccessSpecValidator()
    {
        RuleFor(c => c.RequestId).Must(RequestIdFormat.IsValid)
            .WithMessage("The request id is not a valid agent output folder name.");
        RuleFor(c => c.Workers).NotNull().Must(w => w is { Count: > 0 and <= 50 })
            .WithMessage("Send between 1 and 50 workers.");
        RuleForEach(c => c.Workers).ChildRules(w =>
        {
            w.RuleFor(e => e.UserCode).Must(code => !string.IsNullOrEmpty(code) && code.Length <= 10 && code.All(char.IsAsciiDigit))
                .WithMessage("A worker code must be digits only.");
            w.RuleFor(e => e.WorkCenterIds).Must(ids => ids is null || ids.Count <= 200)
                .WithMessage("At most 200 work centers per worker.");
            w.RuleFor(e => e.NewUser).Must(n => n is null || (Short(n.FirstName) && Short(n.LastName) && Short(n.EngFirstName)
                                                              && Short(n.EngLastName) && Short(n.UserName) && Short(n.Password) && Short(n.Email)))
                .WithMessage("A field of the new user is too long.");
        });
    }

    private static bool Short(string? value) => value is null || value.Length <= 100;
}

internal sealed class GetAccessSpecValidator : AbstractValidator<GetAccessSpecQuery>
{
    public GetAccessSpecValidator()
        => RuleFor(q => q.RequestId).Must(RequestIdFormat.IsValid)
            .WithMessage("The request id is not a valid agent output folder name.");
}
