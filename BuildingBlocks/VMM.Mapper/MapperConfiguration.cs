using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace VMM.Mapper
{
    /// <summary>
    /// Compiles the maps of the given profiles into plans (constructor + member assignments with compiled getters).
    /// Build it once (singleton); <see cref="AssertConfigurationIsValid"/> reports every destination member nothing maps to.
    /// </summary>
    public sealed class MapperConfiguration
    {
        private readonly Dictionary<(Type, Type), ITypeMap> _maps = new();
        private readonly ConcurrentDictionary<(Type, Type), Plan> _plans = new();

        public MapperConfiguration(IEnumerable<MapperProfile> profiles)
        {
            foreach (var map in profiles.SelectMany(p => p.Maps))
                _maps[(map.Source, map.Destination)] = map;   // a later map of the same pair wins
        }

        public MapperConfiguration(params MapperProfile[] profiles) : this((IEnumerable<MapperProfile>)profiles) { }

        public IMapper CreateMapper() => new ObjectMapper(this);

        /// <summary>Throws a <see cref="MappingException"/> listing every member that would stay unmapped or cannot be converted.</summary>
        public void AssertConfigurationIsValid()
        {
            var problems = _maps.Keys.SelectMany(k => PlanFor(k.Item1, k.Item2)!.Problems).ToList();
            if (problems.Count > 0)
                throw new MappingException("Invalid mapping configuration:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => " - " + p)));
        }

        internal bool HasMap(Type source, Type destination) => FindMap(source, destination) != null;

        private ITypeMap? FindMap(Type source, Type destination)
        {
            for (var t = source; t != null; t = t.BaseType)
                if (_maps.TryGetValue((t, destination), out var map)) return map;
            return source.GetInterfaces().Select(i => _maps.GetValueOrDefault((i, destination))).FirstOrDefault(m => m != null);
        }

        internal Plan? PlanFor(Type source, Type destination)
        {
            if (_plans.TryGetValue((source, destination), out var plan)) return plan;
            var map = FindMap(source, destination);
            return map == null ? null : _plans.GetOrAdd((source, destination), _ => new Plan(this, map, source));
        }

        // ---------------------------------------------------------------- conversion of one value

        internal bool CanConvert(Type from, Type to)
        {
            if (to.IsAssignableFrom(from) || HasMap(from, to)) return true;
            var target = Nullable.GetUnderlyingType(to) ?? to;
            var source = Nullable.GetUnderlyingType(from) ?? from;
            if (target.IsAssignableFrom(source)) return true;
            if (target.IsEnum) return source == typeof(string) || source.IsEnum || IsNumeric(source);
            if (target == typeof(string)) return true;
            if (IsNumeric(target) || target == typeof(bool) || target == typeof(DateTime) || target == typeof(Guid))
                return IsNumeric(source) || source.IsEnum || source == typeof(string) || source == typeof(bool);
            return ElementType(to) is { } toItem && ElementType(from) is { } fromItem && CanConvert(fromItem, toItem);
        }

        internal object? Convert(object? value, Type to)
        {
            if (value == null) return to.IsValueType && Nullable.GetUnderlyingType(to) == null ? Activator.CreateInstance(to) : null;
            var from = value.GetType();
            if (to.IsInstanceOfType(value) && !(value is IEnumerable && from != typeof(string) && ElementType(to) is { } e && HasMapFromElements(value, e)))
                return value;
            if (PlanFor(from, to) is { } plan) return plan.Create(value);

            var target = Nullable.GetUnderlyingType(to) ?? to;
            try
            {
                if (target.IsInstanceOfType(value)) return value;
                if (target.IsEnum)
                    return value is string s ? Enum.Parse(target, s, ignoreCase: true) : Enum.ToObject(target, System.Convert.ChangeType(value, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture));
                if (target == typeof(string)) return System.Convert.ToString(value, CultureInfo.InvariantCulture);
                if (target == typeof(Guid)) return value is string g ? Guid.Parse(g) : throw new InvalidCastException();
                if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
                    return System.Convert.ChangeType(value is Enum ? System.Convert.ChangeType(value, Enum.GetUnderlyingType(from)) : value, target, CultureInfo.InvariantCulture);
                if (value is IEnumerable items && ElementType(to) is { } item) return Collection(items, to, item);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                throw new MappingException($"Cannot convert '{value}' ({from.Name}) to {to.Name}.", ex);
            }
            throw new MappingException($"No map from {from.Name} to {to.Name}. Add CreateMap<{from.Name}, {to.Name}>() to a profile.");
        }

        private bool HasMapFromElements(object value, Type element) =>
            ((IEnumerable)value).Cast<object?>().FirstOrDefault(v => v != null) is { } first && !element.IsInstanceOfType(first) && HasMap(first.GetType(), element);

        private object Collection(IEnumerable items, Type to, Type item)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item))!;
            foreach (var v in items) list.Add(Convert(v, item));
            if (to.IsArray)
            {
                var array = Array.CreateInstance(item, list.Count);
                list.CopyTo(array, 0);
                return array;
            }
            if (to.IsAssignableFrom(list.GetType())) return list;
            if (to.IsClass && !to.IsAbstract && Activator.CreateInstance(to) is IList target)   // e.g. Collection<T>, HashSet via IList not supported
            {
                foreach (var v in list) target.Add(v);
                return target;
            }
            throw new MappingException($"Cannot create a {to.Name} collection.");
        }

        internal static Type? ElementType(Type t)
        {
            if (t == typeof(string)) return null;
            if (t.IsArray) return t.GetElementType();
            var enumerable = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? t
                : t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable?.GetGenericArguments()[0];
        }

        private static bool IsNumeric(Type t) => Type.GetTypeCode(t) is >= TypeCode.SByte and <= TypeCode.Decimal;

        // ---------------------------------------------------------------- plan of one map

        internal sealed class Plan
        {
            private readonly MapperConfiguration _config;
            private readonly Type _destination;
            private readonly Func<object, object>? _factory;
            private readonly ConstructorInfo? _ctor;
            private readonly (Func<object, object?>? Get, Type Type, object? Default)[] _ctorArgs = Array.Empty<(Func<object, object?>?, Type, object?)>();
            private readonly (Func<object, object?> Get, PropertyInfo Property)[] _members;
            public List<string> Problems { get; } = new();

            public Plan(MapperConfiguration config, ITypeMap map, Type source)
            {
                _config = config;
                var destination = _destination = map.Destination;
                var name = $"{source.Name} -> {destination.Name}";
                var sourceProps = source.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                    .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                Func<object, object?>? Resolve(string member, Type type)
                {
                    if (map.Resolvers.TryGetValue(member, out var r)) return r;
                    if (!sourceProps.TryGetValue(member, out var p)) return null;
                    if (!config.CanConvert(p.PropertyType, type) && !(ElementType(p.PropertyType) != null && ElementType(type) != null))
                        Problems.Add($"{name}: {member} ({p.PropertyType.Name}) cannot be converted to {type.Name}");
                    return Getter(p);
                }

                var constructed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (map.Constructor != null) _factory = map.Constructor;
                else if (destination.IsValueType || destination.GetConstructor(Type.EmptyTypes) != null) _ctor = destination.GetConstructor(Type.EmptyTypes);
                else
                {
                    // the public constructor with the most parameters that can all be filled
                    foreach (var ctor in destination.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
                    {
                        var ps = ctor.GetParameters();
                        var args = ps.Select(p => (Get: map.Ignored.Contains(p.Name!) ? null : Resolve(p.Name!, p.ParameterType), p.ParameterType,
                            Default: p.HasDefaultValue ? p.DefaultValue : null, p)).ToArray();
                        if (args.All(a => a.Get != null || a.p.HasDefaultValue || map.Ignored.Contains(a.p.Name!)))
                        {
                            _ctor = ctor;
                            _ctorArgs = args.Select(a => (a.Get, a.ParameterType, a.Default)).ToArray();
                            foreach (var p in ps) constructed.Add(p.Name!);
                            break;
                        }
                    }
                    if (_ctor == null) Problems.Add($"{name}: no public constructor whose parameters can all be mapped (use ConstructUsing)");
                }

                var members = new List<(Func<object, object?>, PropertyInfo)>();
                foreach (var prop in destination.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.SetMethod is not { IsPublic: true } || prop.GetIndexParameters().Length > 0 || constructed.Contains(prop.Name) || map.Ignored.Contains(prop.Name))
                        continue;
                    var get = Resolve(prop.Name, prop.PropertyType);
                    if (get == null) Problems.Add($"{name}: nothing maps to {prop.Name} (add ForMember or Ignore)");
                    else members.Add((get, prop));
                }
                foreach (var key in map.Resolvers.Keys.Concat(map.Ignored))
                    if (!constructed.Contains(key) && destination.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) == null)
                        Problems.Add($"{name}: {destination.Name} has no member {key}");
                _members = members.ToArray();
            }

            public object Create(object source)
            {
                object destination;
                if (_factory != null) destination = _factory(source);
                else if (_ctor == null)
                    destination = _destination.IsValueType ? Activator.CreateInstance(_destination)! : throw new MappingException(Problems.FirstOrDefault() ?? "No constructor.");
                else destination = _ctor.Invoke(_ctorArgs.Select(a => a.Get == null ? a.Default ?? Default(a.Type) : _config.Convert(a.Get(source), a.Type)).ToArray());
                return Fill(source, destination);
            }

            public object Fill(object source, object destination)
            {
                foreach (var (get, prop) in _members)
                {
                    try { prop.SetValue(destination, _config.Convert(get(source), prop.PropertyType)); }
                    catch (MappingException ex) { throw new MappingException($"{source.GetType().Name}.{prop.Name}: {ex.Message}", ex); }
                }
                return destination;
            }

            private static object? Default(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

            private static Func<object, object?> Getter(PropertyInfo p)
            {
                var o = Expression.Parameter(typeof(object), "o");
                var body = Expression.Convert(Expression.Property(Expression.Convert(o, p.DeclaringType!), p), typeof(object));
                return Expression.Lambda<Func<object, object?>>(body, o).Compile();
            }
        }
    }

    internal sealed class ObjectMapper : IMapper
    {
        private readonly MapperConfiguration _config;
        public ObjectMapper(MapperConfiguration config) => _config = config;

        public TDestination Map<TDestination>(object source) => (TDestination)_config.Convert(source ?? throw new ArgumentNullException(nameof(source)), typeof(TDestination))!;

        public TDestination Map<TSource, TDestination>(TSource source) => source == null ? default! : (TDestination)_config.Convert(source, typeof(TDestination))!;

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination) where TDestination : class
        {
            ArgumentNullException.ThrowIfNull(source);
            var plan = _config.PlanFor(source.GetType(), typeof(TDestination)) ?? throw new MappingException($"No map from {source.GetType().Name} to {typeof(TDestination).Name}.");
            return (TDestination)plan.Fill(source, destination);
        }

        public List<TDestination> MapList<TSource, TDestination>(IEnumerable<TSource> source) => source.Select(Map<TSource, TDestination>).ToList();
    }
}
