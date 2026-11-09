using System.Text;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;

namespace Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

internal sealed class ProcessAccessRequestHandler(IAccessRequestAgent agent, IMailFileReader mailReader)
    : ICommandHandler<ProcessAccessRequestCommand, AccessRequestResult>
{
    public async Task<Result<AccessRequestResult>> Handle(ProcessAccessRequestCommand command, CancellationToken ct)
    {
        var pdfs = command.PdfFiles ?? [];
        var hasText = !string.IsNullOrWhiteSpace(command.EmailText);
        var hasMails = command.MailFiles is { Count: > 0 };

        AgentInput input;
        if (pdfs.Count == 1 && !hasText && !hasMails)
        {
            // טופס הסמכה יחיד, כמו תמיד
            input = new AgentInput(".pdf", pdfs[0].Content, Server: command.Server);
        }
        else
        {
            var composed = Compose(command);
            if (composed.Error is not null)
                return Result<AccessRequestResult>.Failure(composed.Error);

            // טפסי PDF לצד טקסט/מיילים (או כמה טפסים): הם קבצים נלווים שהסוכן קורא בעצמו
            input = new AgentInput(".md", Encoding.UTF8.GetBytes(composed.Text!),
                pdfs.Count == 0 ? null : pdfs.Select(p => new AgentAttachment(".pdf", p.Content)).ToList(),
                command.Server);
        }

        var run = await agent.Process(input, ct);

        if (!run.Completed)
            return Result<AccessRequestResult>.Failure(
                Error.Failure("AccessRequest.AgentFailed", "The agent did not finish. See the server log."));

        // הסוכן לא כתב spec (שאלה פתוחה, עובד חסום, או שאין עובד חדש): התשובה שלו היא התוצאה, והדף מציג אותה.
        // כשאין גם תשובה זה כשל
        if (run.RequestId is null)
            return string.IsNullOrWhiteSpace(run.Summary)
                ? Result<AccessRequestResult>.Failure(
                    Error.Failure("AccessRequest.NoOutput", "The agent finished without writing a plan."))
                : Result<AccessRequestResult>.Success(
                    new AccessRequestResult(string.Empty, run.Summary, null, null, null, false, run.Server, run.CanRunScript));

        return Result<AccessRequestResult>.Success(
            new AccessRequestResult(run.RequestId, run.Summary, run.Plan, run.Script, run.Explanation,
                run.Script is not null && AccessScriptGuard.HasWrites(run.Script), run.Server, run.CanRunScript));
    }

    // כל מה שהאדם נתן הופך למסמך אחד לסוכן: הטקסט מהתיבה קודם (בדרך כלל הוראות והערות), ואחריו כל מייל
    // לפי הסדר, כל אחד עם כותרת. טקסט בלי מיילים נשלח כמו שהוא
    private (string? Text, Error? Error) Compose(ProcessAccessRequestCommand command)
    {
        var mails = command.MailFiles ?? [];
        if (mails.Count == 0)
            return (command.EmailText ?? string.Empty, null);

        var document = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(command.EmailText))
        {
            document.AppendLine("=== Text typed by the person who sent this request ===");
            document.AppendLine(command.EmailText.Trim());
            document.AppendLine();
        }

        for (var i = 0; i < mails.Count; i++)
        {
            var text = mailReader.ToText(mails[i].Content, mails[i].Name);
            if (string.IsNullOrWhiteSpace(text))
            {
                var reason = mailReader.Diagnose(mails[i].Content, mails[i].Name);
                return (null, Error.Validation("AccessRequest.UnreadableMail",
                    $"Mail {i + 1} of {mails.Count} could not be read, or it has no text" +
                    (string.IsNullOrWhiteSpace(reason) ? "." : $" ({reason}).")));
            }

            document.AppendLine($"=== Mail {i + 1} of {mails.Count} ===");
            document.AppendLine(text.Trim());
            document.AppendLine();
        }

        if (document.Length > MaxCombinedLength)
            return (null, Error.Validation("AccessRequest.TooMuchText",
                $"All the mails together are longer than {MaxCombinedLength:N0} characters. Send fewer mails."));

        return (document.ToString(), null);
    }

    private const int MaxCombinedLength = 100_000;
}
