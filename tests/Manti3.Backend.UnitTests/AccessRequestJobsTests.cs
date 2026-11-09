using Manti3.Backend.Api.Endpoints.AccessRequests;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class AccessRequestJobsTests
{
    private static readonly ProcessAccessRequestCommand Command = new("text", null);
    private static readonly AccessRequestResult Ok = new("web-20261104-100000", "summary", null, "script", "explanation", true);

    private static AccessRequestJobs Jobs(ICommandHandler<ProcessAccessRequestCommand, AccessRequestResult> handler)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        return new AccessRequestJobs(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AccessRequestJobs>.Instance);
    }

    private static async Task<JobSnapshot> Finished(AccessRequestJobs jobs, string id)
    {
        for (var i = 0; i < 200; i++)
        {
            var snapshot = jobs.Get(id)!;
            if (snapshot.Status != "running")
                return snapshot;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The job did not finish.");
    }

    private sealed class Handler(Func<Task<Result<AccessRequestResult>>> run)
        : ICommandHandler<ProcessAccessRequestCommand, AccessRequestResult>
    {
        public Task<Result<AccessRequestResult>> Handle(ProcessAccessRequestCommand command, CancellationToken ct) => run();
    }

    [Fact]
    public async Task Start_ReturnsAtOnce_AndTheJobIsRunningUntilTheHandlerFinishes()
    {
        var release = new TaskCompletionSource();
        var jobs = Jobs(new Handler(async () =>
        {
            await release.Task;
            return Result<AccessRequestResult>.Success(Ok);
        }));

        var id = jobs.Start(Command);

        jobs.Get(id)!.Status.ShouldBe("running");
        release.SetResult();
        var done = await Finished(jobs, id);
        done.Status.ShouldBe("done");
        done.Result.ShouldBe(Ok);
    }

    [Fact]
    public async Task Start_HandlerReturnsAFailure_TheJobFailsWithItsCodeAndDescription()
    {
        var jobs = Jobs(new Handler(() => Task.FromResult(
            Result<AccessRequestResult>.Failure(Error.Failure("AccessRequest.AgentFailed", "The agent did not finish.")))));

        var failed = await Finished(jobs, jobs.Start(Command));

        failed.Status.ShouldBe("failed");
        failed.ErrorTitle.ShouldBe("AccessRequest.AgentFailed");
        failed.ErrorDetail.ShouldBe("The agent did not finish.");
        failed.Result.ShouldBeNull();
    }

    [Fact]
    public async Task Start_HandlerThrows_TheJobFailsWithoutLeakingTheExceptionMessage()
    {
        var jobs = Jobs(new Handler(() => throw new InvalidOperationException("worker 10001 secret detail")));

        var failed = await Finished(jobs, jobs.Start(Command));

        failed.Status.ShouldBe("failed");
        failed.ErrorDetail.ShouldNotBeNull().ShouldNotContain("10001");
        failed.ErrorDetail.ShouldNotContain("secret");
    }

    [Fact]
    public void Get_UnknownId_IsNull()
        => Jobs(new Handler(() => Task.FromResult(Result<AccessRequestResult>.Success(Ok)))).Get("nope").ShouldBeNull();

    [Fact]
    public void Start_TwoJobs_GetDifferentUnguessableIds()
    {
        var jobs = Jobs(new Handler(() => Task.FromResult(Result<AccessRequestResult>.Success(Ok))));

        var a = jobs.Start(Command);
        var b = jobs.Start(Command);

        a.ShouldNotBe(b);
        a.Length.ShouldBe(32);
        a.ShouldMatch("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task TryStart_ClientChosenId_RunsUnderThatId()
    {
        var jobs = Jobs(new Handler(() => Task.FromResult(Result<AccessRequestResult>.Success(Ok))));
        var id = new string('a', 32);

        jobs.TryStart(Command, id).ShouldBe(StartOutcome.Started);

        (await Finished(jobs, id)).Status.ShouldBe("done");
    }

    [Theory]
    [InlineData("short")]
    [InlineData("../../etc/passwd")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void TryStart_BadIdShape_IsRejected(string id)
    {
        var jobs = Jobs(new Handler(() => Task.FromResult(Result<AccessRequestResult>.Success(Ok))));

        jobs.TryStart(Command, id).ShouldBe(StartOutcome.InvalidId);
        jobs.Get(id).ShouldBeNull();
    }

    [Fact]
    public void TryStart_ExistingId_IsRejectedAndNotOverwritten()
    {
        var jobs = Jobs(new Handler(() => Task.FromResult(Result<AccessRequestResult>.Success(Ok))));
        var id = new string('b', 32);

        jobs.TryStart(Command, id).ShouldBe(StartOutcome.Started);

        jobs.TryStart(Command, id).ShouldBe(StartOutcome.AlreadyExists);
    }
}
