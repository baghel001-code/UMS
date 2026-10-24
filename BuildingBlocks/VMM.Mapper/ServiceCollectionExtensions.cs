using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace VMM.Mapper
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a singleton <see cref="IMapper"/> built from every <see cref="MapperProfile"/> in the assemblies. The configuration is validated
        /// at registration, so a broken map fails at start-up instead of on first use.
        /// </summary>
        public static IServiceCollection AddObjectMapper(this IServiceCollection services, params Assembly[] assemblies)
        {
            var profiles = assemblies.Distinct()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(MapperProfile).IsAssignableFrom(t) && t is { IsAbstract: false } && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (MapperProfile)Activator.CreateInstance(t)!);
            var config = new MapperConfiguration(profiles);
            config.AssertConfigurationIsValid();
            services.AddSingleton(config);
            services.AddSingleton(config.CreateMapper());
            return services;
        }
    }
}
