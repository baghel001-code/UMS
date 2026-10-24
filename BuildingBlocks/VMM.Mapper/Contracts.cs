namespace VMM.Mapper
{
    /// <summary>Maps objects between types configured in <see cref="MapperProfile"/>s.</summary>
    public interface IMapper
    {
        /// <summary>Creates a <typeparamref name="TDestination"/> from <paramref name="source"/> (its runtime type picks the map).</summary>
        TDestination Map<TDestination>(object source);

        TDestination Map<TSource, TDestination>(TSource source);

        /// <summary>Copies onto an existing object (its writable properties only).</summary>
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination) where TDestination : class;

        List<TDestination> MapList<TSource, TDestination>(IEnumerable<TSource> source);
    }

    public sealed class MappingException : Exception
    {
        public MappingException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
