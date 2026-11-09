using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.BuildScript;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;

namespace Manti3.Backend.Application.Features.AccessRequests.EditSpec;

internal sealed class GetAccessSpecHandler(IAgentOutput output, IAccessLookups lookups) : IQueryHandler<GetAccessSpecQuery, SpecView>
{
    public async Task<Result<SpecView>> Handle(GetAccessSpecQuery query, CancellationToken ct)
    {
        var json = output.ReadSpec(query.RequestId);
        var spec = json is null ? null : AccessScriptBuilder.Parse(json);

        if (spec?.Workers is null)
            return Result<SpecView>.Failure(Error.NotFound(
                "AccessRequest.SpecNotFound", "There is no editable spec for this request."));

        var server = output.ReadServerName(query.RequestId);
        var resolved = await SpecMapper.ResolveNames(lookups, server, json!, ct);
        return Result<SpecView>.Success(SpecMapper.ToView(
            spec, await SpecMapper.ReadExisting(lookups, server, spec, ct), resolved.Names));
    }
}

internal static class SpecMapper
{
    // היישומים שכבר יש לעובדים הקיימים בבקשה. תצוגה בלבד: אם הקריאה נכשלת, הטבלה נטענת בלעדיהם (בלי לחסום את העריכה)
    public static async Task<IReadOnlyDictionary<long, IReadOnlyList<int>>?> ReadExisting(
        IAccessLookups lookups, string? server, ScriptSpec spec, CancellationToken ct)
    {
        var ids = spec.Workers!.Where(w => w.NewUser is null && w.UserId is not null).Select(w => w.UserId!.Value).ToList();
        var read = await lookups.ExistingApplications(server, ids, ct);
        return read.IsSuccess ? read.Value : null;
    }

    // שמות המשתמש המחושבים של העובדים החדשים (הראשון הפנוי). אם הקריאה ל-DB נכשלת: מפה ריקה (השם יהיה שם פרטי + אות אחת)
    // ו-Failure מוחזר כדי שמי שרוצה (עריכה) יכשיל; מי שרק מציג מתעלם ממנו
    public static async Task<(IReadOnlyDictionary<string, string> Names, Error? Failure)> ResolveNames(
        IAccessLookups lookups, string? server, string specJson, CancellationToken ct)
    {
        var candidates = AccessScriptBuilder.CandidateUserNames(specJson);
        if (candidates.Count == 0)
            return (new Dictionary<string, string>(), null);

        var taken = await lookups.TakenUserNames(server, candidates, ct);
        return taken.IsSuccess
            ? (AccessScriptBuilder.ResolveUserNames(specJson, taken.Value), null)
            : (new Dictionary<string, string>(), taken.Error);
    }

    public static SpecView ToView(ScriptSpec spec, IReadOnlyDictionary<long, IReadOnlyList<int>>? existing = null,
        IReadOnlyDictionary<string, string>? resolvedNames = null)
        // עובד חדש (נוצר לו משתמש) ועובד קיים (רק מוסיפים לו מרכזי עבודה ויישומים). עובד שאין לו userId ולא newUser לא מוצג
        => new(spec.Workers!.Where(w => w.NewUser is not null || w.UserId is not null).Select(w => new WorkerView(
            w.UserCode ?? string.Empty,
            w.NewUser is not null,
            w.NewUser is { } u
                ? new NewUserFields(u.FirstName, u.LastName,
                    u.UserName
                        ?? (resolvedNames is not null && w.UserCode is not null && resolvedNames.TryGetValue(w.UserCode, out var name) ? name : null)
                        ?? EnglishNames.UserName(u.EngFirstName, u.EngLastName) ?? string.Empty,
                    u.Password ?? w.UserCode, u.GroupId, u.DepartmentId, u.EngFirstName, u.EngLastName, u.Email)
                : null,
            (w.WorkCenters ?? []).Select(c => c.Id).ToList(),
            !w.Skip,
            w.NewUser is null ? w.Name : null,
            (w.AddScreens ?? []).Select(s => s.ObjId).ToList(),
            w.UserId is { } id && existing is not null && existing.TryGetValue(id, out var have) ? have : [])).ToList());
}

internal sealed class GetAccessLookupsHandler(IAccessLookups lookups) : IQueryHandler<GetAccessLookupsQuery, AccessLookupData>
{
    public Task<Result<AccessLookupData>> Handle(GetAccessLookupsQuery query, CancellationToken ct)
        => lookups.Get(query.Server, ct);
}
