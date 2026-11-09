using System.Collections.Concurrent;
using System.Security.Cryptography;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

namespace Manti3.Backend.Api.Endpoints.AccessRequests;

// ריצות סוכן ברקע, בזיכרון. הדף הקופץ של Outlook מתחיל ריצה ושואל את הסטטוס כל כמה שניות, במקום להחזיק בקשה
// פתוחה דקה. התוצאה מכילה נתוני עובדים, ולכן נשארת בזיכרון בלבד ונמחקת אחרי שעתיים; ה-id אקראי (128 ביט).
// כמו שאר נקודות הקצה של הסוכן: רק בסביבת פיתוח, על המחשב המקומי
public sealed class AccessRequestJobs(IServiceScopeFactory scopes, ILogger<AccessRequestJobs> logger)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public string Start(ProcessAccessRequestCommand command)
    {
        Purge();

        string id;
        var job = new Job(DateTime.UtcNow);
        do
        {
            id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        } while (!_jobs.TryAdd(id, job));

        // ריצה אחת של הסוכן בכל פעם (נאכף ב-ClaudeCliAgent): ריצות נוספות ממתינות בתור
        _ = Task.Run(() => Run(job, command));
        return id;
    }

    // Outlook פותח את החלון הקופץ מיד (לפני שהמייל נשלח), ולכן התוסף בוחר את ה-id בעצמו. חייב להיות אקראי באורך
    // 32 תווי hex (128 ביט), אחרת הוא נדחה; id שכבר קיים נדחה גם הוא ולא דורס ריצה
    public StartOutcome TryStart(ProcessAccessRequestCommand command, string requestedId)
    {
        if (!IdFormat.IsMatch(requestedId))
            return StartOutcome.InvalidId;

        Purge();

        var job = new Job(DateTime.UtcNow);
        if (!_jobs.TryAdd(requestedId, job))
            return StartOutcome.AlreadyExists;

        _ = Task.Run(() => Run(job, command));
        return StartOutcome.Started;
    }

    private static readonly System.Text.RegularExpressions.Regex IdFormat = new("^[0-9a-f]{32}$");

    public JobSnapshot? Get(string id)
        => _jobs.TryGetValue(id, out var job)
            ? new JobSnapshot(job.Status, job.Result, job.ErrorTitle, job.ErrorDetail,
                (int)((job.FinishedAt ?? DateTime.UtcNow) - job.StartedAt).TotalSeconds)
            : null;

    private async Task Run(Job job, ProcessAccessRequestCommand command)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessAccessRequestCommand, AccessRequestResult>>();
            var result = await handler.Handle(command, CancellationToken.None);

            if (result.IsSuccess)
                job.Succeed(result.Value);
            else
                job.Fail(result.Error!.Code, result.Error.Description);
        }
        catch (Exception ex)
        {
            // סוג החריגה בלבד: ההודעה עלולה להכיל נתוני עובדים
            logger.LogError("Access request job failed unexpectedly ({Type})", ex.GetType().Name);
            job.Fail("AccessRequest.Unexpected", "The agent run failed unexpectedly. See the server log.");
        }
    }

    private void Purge()
    {
        foreach (var (id, job) in _jobs)
            if (job.FinishedAt is { } finished && DateTime.UtcNow - finished > KeepFor)
                _jobs.TryRemove(id, out _);
    }

    private sealed class Job(DateTime startedAt)
    {
        public DateTime StartedAt { get; } = startedAt;
        public DateTime? FinishedAt { get; private set; }
        public string Status { get; private set; } = "running";
        public AccessRequestResult? Result { get; private set; }
        public string? ErrorTitle { get; private set; }
        public string? ErrorDetail { get; private set; }

        public void Succeed(AccessRequestResult result)
        {
            Result = result;
            Status = "done";
            FinishedAt = DateTime.UtcNow;
        }

        public void Fail(string title, string detail)
        {
            ErrorTitle = title;
            ErrorDetail = detail;
            Status = "failed";
            FinishedAt = DateTime.UtcNow;
        }
    }
}

public enum StartOutcome { Started, InvalidId, AlreadyExists }

// Status: running | done | failed
public sealed record JobSnapshot(string Status, AccessRequestResult? Result, string? ErrorTitle, string? ErrorDetail, int ElapsedSeconds);
