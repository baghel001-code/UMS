using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace VMM.Mediator
{
    public sealed class MediatorOptions
    {
        internal List<Assembly> Assemblies { get; } = new();
        internal List<Type> Behaviors { get; } = new();

        /// <summary>Lifetime of the handlers (default transient; handlers take scoped repositories).</summary>
        public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Transient;

        public MediatorOptions RegisterServicesFromAssembly(Assembly assembly)
        {
            if (!Assemblies.Contains(assembly)) Assemblies.Add(assembly);
            return this;
        }

        public MediatorOptions RegisterServicesFromAssemblyContaining<T>() => RegisterServicesFromAssembly(typeof(T).Assembly);

        /// <summary>Adds an open generic behaviour, e.g. <c>typeof(ValidationBehavior&lt;,&gt;)</c>. Order of calls = order of execution.</summary>
        public MediatorOptions AddOpenBehavior(Type openBehaviorType)
        {
            if (!openBehaviorType.IsGenericTypeDefinition ||
                !openBehaviorType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
                throw new ArgumentException($"{openBehaviorType.Name} must be an open generic IPipelineBehavior<,>.", nameof(openBehaviorType));
            Behaviors.Add(openBehaviorType);
            return this;
        }
    }

    public static class ServiceCollectionExtensions
    {
        /// <summary>Registers <see cref="IMediator"/>/<see cref="ISender"/>/<see cref="IPublisher"/>, every request and notification handler found in the given assemblies, and the behaviours.</summary>
        public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorOptions> configure)
        {
            var options = new MediatorOptions();
            configure(options);

            services.TryAddTransient<IMediator, Mediator>();
            services.TryAddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
            services.TryAddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

            foreach (var (service, implementation) in FindHandlers(options.Assemblies))
                services.TryAddEnumerable(new ServiceDescriptor(service, implementation, options.HandlerLifetime));

            foreach (var behavior in options.Behaviors)
                services.Add(new ServiceDescriptor(typeof(IPipelineBehavior<,>), behavior, ServiceLifetime.Transient));
            return services;
        }

        internal static IEnumerable<(Type Service, Type Implementation)> FindHandlers(IEnumerable<Assembly> assemblies) =>
            from assembly in assemblies
            from type in LoadableTypes(assembly)
            where type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            from service in type.GetInterfaces()
            where service.IsGenericType &&
                  (service.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) || service.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            select (service, type);

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
        }
    }
}
