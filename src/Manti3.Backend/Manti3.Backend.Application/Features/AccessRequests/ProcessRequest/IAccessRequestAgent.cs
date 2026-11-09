namespace Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

// פורט: המימוש ב-Infrastructure מפעיל את access-request-agent של Claude Code
public interface IAccessRequestAgent
{
    Task<AgentRun> Process(AgentInput input, CancellationToken ct);
}

// Extension: ".md" למסמך טקסט (טקסט ומיילים), ".pdf" כשהבקשה היא טופס יחיד ותו לא.
// Attachments: קבצים נלווים (טפסי PDF) שהסוכן קורא בעצמו, כשיש יחד איתם טקסט, מיילים או עוד טפסים
// Server = שם שרת מהרשימה (IAgentServers); null = ברירת המחדל
public sealed record AgentInput(string Extension, byte[] Content, IReadOnlyList<AgentAttachment>? Attachments = null,
    string? Server = null);

public sealed record AgentAttachment(string Extension, byte[] Content);

// RequestId = שם התיקייה ב-.agent-output שהסוכן כתב אליה (null אם לא כתב)
// Explanation = explanation.md: מה הסקריפט יעשה, בעברית פשוטה, לאדם שמריץ אותו
public sealed record AgentRun(bool Completed, string Summary, string? RequestId, string? Plan, string? Script,
    string? Explanation = null, string? Server = null, bool CanRunScript = true);
