using FluentValidation;

namespace Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

internal sealed class ProcessAccessRequestValidator : AbstractValidator<ProcessAccessRequestCommand>
{
    private const int MaxTextLength = 20_000;
    private const int MaxPdfBytes = 5 * 1024 * 1024;
    private const int MaxPdfs = 5;
    private const int MaxMailBytes = 10 * 1024 * 1024;
    private const int MaxMails = 10;

    public ProcessAccessRequestValidator()
    {
        // כל שילוב מותר, אבל חייב להיות משהו לשלוח
        RuleFor(c => c)
            .Must(c => HasText(c) || HasMails(c) || HasPdfs(c))
            .WithMessage("Send the email text, mail files (.msg / .eml), PDF forms, or any mix of them.");

        RuleFor(c => c.EmailText!.Length)
            .LessThanOrEqualTo(MaxTextLength)
            .When(HasText)
            .WithMessage($"The email text is longer than {MaxTextLength} characters.");

        RuleFor(c => c.PdfFiles!.Count)
            .LessThanOrEqualTo(MaxPdfs)
            .When(HasPdfs)
            .WithMessage($"At most {MaxPdfs} PDF files at a time.");

        RuleForEach(c => c.PdfFiles!)
            .Must(p => IsPdf(p.Content)).WithMessage("Every PDF file must be a real PDF.")
            .Must(p => p.Content.Length <= MaxPdfBytes).WithMessage("A PDF is larger than 5 MB.")
            .When(HasPdfs);

        RuleFor(c => c.MailFiles!.Count)
            .LessThanOrEqualTo(MaxMails)
            .When(HasMails)
            .WithMessage($"At most {MaxMails} mail files at a time.");

        RuleForEach(c => c.MailFiles!)
            .Must(m => IsMailFileName(m.Name)).WithMessage("Every mail file must be .msg or .eml.")
            .Must(m => m.Content.Length <= MaxMailBytes).WithMessage("A mail file is larger than 10 MB.")
            .When(HasMails);
    }

    private static bool HasText(ProcessAccessRequestCommand c) => !string.IsNullOrWhiteSpace(c.EmailText);

    private static bool HasPdfs(ProcessAccessRequestCommand c) => c.PdfFiles is { Count: > 0 };

    private static bool HasMails(ProcessAccessRequestCommand c) => c.MailFiles is { Count: > 0 };

    private static bool IsMailFileName(string name)
        => name.EndsWith(".msg", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase);

    // כל קובץ PDF מתחיל ב-%PDF
    private static bool IsPdf(byte[] content)
        => content.Length >= 4 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F';
}
