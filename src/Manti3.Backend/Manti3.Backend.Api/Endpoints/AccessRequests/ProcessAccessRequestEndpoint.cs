using Manti3.Backend.Api.Extensions;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

namespace Manti3.Backend.Api.Endpoints.AccessRequests;

// מפעיל את סוכן ההרשאות על מייל או טופס PDF ומחזיר תוכנית וסקריפט (שלא הורץ)
internal sealed class ProcessAccessRequestEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        // הסוכן ניגש ל-DB ואין עדיין הזדהות (פער ידוע): רק בסביבת פיתוח, על המחשב המקומי
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return;

        app.MapPost("/api/access-requests", async (
                ProcessAccessRequestBody body,
                ICommandHandler<ProcessAccessRequestCommand, AccessRequestResult> handler,
                IAgentServers servers,
                CancellationToken ct) =>
            servers.Find(body.Server) is not { Ready: true }
                ? UnknownServer()
                : (await handler.Handle(body.ToCommand(), ct)).ToHttpResult())
            .WithName("ProcessAccessRequest")
            .WithTags("AccessRequests");

        // הרשימה לתפריט הבחירה בדף: השם, אם זו ברירת המחדל, והאם מותר עליו "הרץ סקריפט". בלי כתובות שרת
        app.MapGet("/api/access-requests/servers", (IAgentServers servers) =>
                servers.All.Select(s => new
                {
                    name = s.Name,
                    isDefault = s == servers.Default,
                    canRunScripts = s.CanRunScripts,
                    ready = s.Ready,
                }))
            .WithName("GetAccessRequestServers")
            .WithTags("AccessRequests");
    }

    internal static IResult UnknownServer()
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "AccessRequest.UnknownServer",
            detail: "The server is not available: it is not in the list of allowed servers, or its login is not configured.");
}

// טקסט, קבצי PDF וקבצי מייל (.msg/.eml שנגררו מ-Outlook): הקבצים מגיעים כ-base64 בתוך ה-JSON
internal sealed record ProcessAccessRequestBody(
    string? EmailText, List<FileBody>? PdfFiles = null, List<FileBody>? MailFiles = null, string? Server = null)
{
    public ProcessAccessRequestCommand ToCommand()
        => new(EmailText,
            PdfFiles?.Select(p => new PdfFileInput(p.Name ?? string.Empty, DecodeBase64(p.Base64) ?? [])).ToList(),
            MailFiles?.Select(m => new MailFileInput(m.Name ?? string.Empty, DecodeBase64(m.Base64) ?? [])).ToList(),
            Server);

    // base64 לא תקין הופך לבייט אחד שאינו קובץ תקין, כדי שהולידציה תחזיר 400 ולא 500
    private static byte[]? DecodeBase64(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
            return null;

        var buffer = new byte[base64.Length];
        return Convert.TryFromBase64String(base64, buffer, out var length) ? buffer[..length] : [0];
    }
}

internal sealed record FileBody(string? Name, string? Base64);
