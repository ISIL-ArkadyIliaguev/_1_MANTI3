using System.Text.RegularExpressions;
using FluentValidation;

namespace Manti3.Backend.Application.Features.AccessRequests.RunScript;

internal sealed partial class RunAccessScriptValidator : AbstractValidator<RunAccessScriptCommand>
{
    public RunAccessScriptValidator()
    {
        // רק שם תיקייה שהסוכן יוצר: yyyyMMdd-HHmm, או web-yyyyMMdd-HHmmss (על שם קובץ הקלט), עם סיומת -2 אפשרית.
        // אין דרך לצאת מ-.agent-output
        RuleFor(c => c.RequestId)
            .Must(id => !string.IsNullOrEmpty(id) && RequestFolder().IsMatch(id))
            .WithMessage("The request id is not a valid agent output folder name.");
    }

    [GeneratedRegex(@"^(web-)?\d{8}-\d{4,6}(-\d+)?$")]
    private static partial Regex RequestFolder();
}
