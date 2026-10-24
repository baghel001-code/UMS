using System.Linq.Expressions;
using System.Reflection;

namespace VMM.Mapper
{
    /// <summary>
    /// Groups the maps of one area. Members with the same name (case-insensitive) map by convention; use
    /// <see cref="TypeMap{TSource, TDestination}.ForMember{TMember}"/> and <see cref="TypeMap{TSource, TDestination}.Ignore{TMember}"/>
    /// for the rest. Records and other immutable types are created through their constructor.
    /// </summary>
    public abstract class MapperProfile
    {
        internal List<ITypeMap> Maps { get; } = new();

        protected TypeMap<TSource, TDestination> CreateMap<TSource, TDestination>()
        {
            var map = new TypeMap<TSource, TDestination>(this);
            Maps.Add(map);
            return map;
        }

        internal TypeMap<TSource, TDestination> Add<TSource, TDestination>() => CreateMap<TSource, TDestination>();
    }

    internal interface ITypeMap
    {
        Type Source { get; }
        Type Destination { get; }
        IReadOnlyDictionary<string, Func<object, object?>> Resolvers { get; }
        IReadOnlySet<string> Ignored { get; }
        Func<object, object>? Constructor { get; }
    }

    public sealed class TypeMap<TSource, TDestination> : ITypeMap
    {
        private readonly MapperProfile _profile;
        private readonly Dictionary<string, Func<object, object?>> _resolvers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
        private Func<object, object>? _constructor;

        internal TypeMap(MapperProfile profile) => _profile = profile;

        public Type Source => typeof(TSource);
        public Type Destination => typeof(TDestination);
        IReadOnlyDictionary<string, Func<object, object?>> ITypeMap.Resolvers => _resolvers;
        IReadOnlySet<string> ITypeMap.Ignored => _ignored;
        Func<object, object>? ITypeMap.Constructor => _constructor;

        /// <summary>Takes a destination member (property or constructor parameter of the same name) from <paramref name="from"/>.</summary>
        public TypeMap<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> member, Func<TSource, TMember> from)
        {
            _resolvers[MemberName(member)] = s => from((TSource)s);
            _ignored.Remove(MemberName(member));
            return this;
        }

        /// <summary>Leaves a destination property untouched (or its constructor parameter at its default).</summary>
        public TypeMap<TSource, TDestination> Ignore<TMember>(Expression<Func<TDestination, TMember>> member)
        {
            _ignored.Add(MemberName(member));
            _resolvers.Remove(MemberName(member));
            return this;
        }

        /// <summary>Creates the destination with this factory; remaining writable properties are still mapped.</summary>
        public TypeMap<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> factory)
        {
            _constructor = s => factory((TSource)s)!;
            return this;
        }

        /// <summary>Adds the opposite map, by convention only.</summary>
        public TypeMap<TDestination, TSource> ReverseMap() => _profile.Add<TDestination, TSource>();

        private static string MemberName<TMember>(Expression<Func<TDestination, TMember>> member)
        {
            var body = member.Body is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand : member.Body;
            return body is MemberExpression { Member: PropertyInfo or FieldInfo } m && m.Expression is ParameterExpression
                ? m.Member.Name
                : throw new ArgumentException($"'{member}' must select a member of {typeof(TDestination).Name}, e.g. d => d.Name.", nameof(member));
        }
    }
}
