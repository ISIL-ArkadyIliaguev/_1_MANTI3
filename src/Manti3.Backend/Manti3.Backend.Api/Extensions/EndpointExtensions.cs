using System.Reflection;
using Manti3.Backend.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Manti3.Backend.Api.Extensions;

public static class EndpointExtensions
{
    // מוצא בזמן אתחול כל מחלקה שמממשת IEndpoint (מחלקה אחת לכל use case)
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var descriptors = assembly.DefinedTypes
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(IEndpoint)))
            .Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t));

        services.TryAddEnumerable(descriptors);
        return services;
    }

    public static IApplicationBuilder MapEndpoints(this WebApplication app)
    {
        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
            endpoint.Map(app);

        return app;
    }
}
