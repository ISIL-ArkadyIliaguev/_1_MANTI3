using System.Reflection;
using Manti3.Backend.Application;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Domain.Items;
using Manti3.Backend.Infrastructure.Persistence;
using NetArchTest.Rules;
using Shouldly;

namespace Manti3.Backend.UnitTests;

// כללי השכבות מה-skill clean-slice-dotnet-service
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(HoningPeripheralItem).Assembly;
    private static readonly Assembly Application = typeof(IApplicationMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(AppDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private const string DomainNs = "Manti3.Backend.Domain";
    private const string ApplicationNs = "Manti3.Backend.Application";
    private const string InfrastructureNs = "Manti3.Backend.Infrastructure";
    private const string ApiNs = "Manti3.Backend.Api";

    [Fact]
    public void Domain_DoesNotReferenceOtherLayersOrFrameworks()
    {
        var result = Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            ApplicationNs, InfrastructureNs, ApiNs, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore").GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureApiOrEfCore()
    {
        var result = Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(
            InfrastructureNs, ApiNs, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "StackExchange.Redis").GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn(ApiNs).GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Api_DoesNotUseEfCoreDirectly()
    {
        var result = Types.InAssembly(Api).ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(IQueryHandler<,>))]
    [InlineData(typeof(ICommandHandler<,>))]
    public void Handlers_AreInternalSealedAndLiveInFeatures(Type handlerInterface)
    {
        // ׳׳—׳׳§׳•׳× ׳׳§׳•׳ ׳ ׳•׳× (׳”-decorators) ׳׳™׳ ׳ handlers ׳©׳ use case
        var handlers = Types.InAssembly(Application)
            .That().AreNotNested()
            .And().ImplementInterface(handlerInterface);

        handlers.Should().NotBePublic().GetResult().IsSuccessful.ShouldBeTrue();
        handlers.Should().BeSealed().GetResult().IsSuccessful.ShouldBeTrue();
        handlers.Should().ResideInNamespaceStartingWith($"{ApplicationNs}.Features").GetResult().IsSuccessful.ShouldBeTrue();
    }
    [Fact]
    public void Endpoints_LiveInApiEndpointsNamespace()
    {
        var result = Types.InAssembly(Api)
            .That().ImplementInterface(typeof(Manti3.Backend.Api.Endpoints.IEndpoint))
            .Should().ResideInNamespaceStartingWith($"{ApiNs}.Endpoints")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }
}
