using System.Security.Cryptography;
using System.Text;
using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Application.Features.AccessRequests.RunScript;

internal sealed class RunAccessScriptHandler(IAgentOutput output, IScriptExecutor executor, IScriptRunLedger ledger)
    : ICommandHandler<RunAccessScriptCommand, ScriptRunResult>
{
    public async Task<Result<ScriptRunResult>> Handle(RunAccessScriptCommand command, CancellationToken ct)
    {
        var script = output.ReadScript(command.RequestId);
        if (script is null)
            return Result<ScriptRunResult>.Failure(
                Error.NotFound("AccessRequest.ScriptNotFound", "There is no script for this request."));

        // המידע בבקשה נקרא משרת אחר (לקריאה בלבד): הסקריפט מתאים לו ולא ל-SQL25DEV, ולכן לא מריצים אותו
        if (output.IsRunBlockedByServer(command.RequestId))
            return Result<ScriptRunResult>.Failure(Error.Conflict(
                "AccessRequest.ReadOnlyServer",
                "This request was prepared from a read-only server. The script can run only for requests prepared from the test server."));

        if (output.IsCommitted(command.RequestId))
            return Result<ScriptRunResult>.Failure(
                Error.Conflict("AccessRequest.AlreadyCommitted", "This script was already run and saved."));

        var notAllowed = AccessScriptGuard.Check(script);
        if (notAllowed is not null)
            return Result<ScriptRunResult>.Failure(Error.Validation("AccessRequest.ScriptNotAllowed", notAllowed));

        // שמירה רק על סקריפט שעבר הרצה לניסיון, ובדיוק באותו תוכן
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        if (command.Commit && !ledger.HasDryRun(command.RequestId, hash))
            return Result<ScriptRunResult>.Failure(Error.Conflict(
                "AccessRequest.DryRunRequired", "Run the script without saving first, and review what it does."));

        var run = await executor.Execute(
            AccessScriptGuard.Prepare(script), command.Commit, output.ReadServerName(command.RequestId), ct);
        if (!run.IsSuccess)
            return run;

        if (command.Commit)
            output.MarkCommitted(command.RequestId);
        else
            ledger.RecordDryRun(command.RequestId, hash);

        return run;
    }
}
