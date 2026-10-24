using AMS.Application.Interfaces;
using AMS.Domain.Exceptions;
using AMS.Domain.Security;

namespace AMS.Application.Services
{
    /// <summary>
    /// Shared plumbing of the AMS use cases: the signed-in actor, the UMS guard and the business clock.
    /// Use cases never look at roles; they describe the action and the record to UMS and act only on an Allow.
    /// </summary>
    public abstract class UseCaseBase
    {
        protected readonly IAccessGuard Guard;
        protected readonly ICurrentActor Current;
        protected readonly IBusinessClock Clock;

        protected UseCaseBase(IAccessGuard guard, ICurrentActor current, IBusinessClock clock)
        {
            Guard = guard;
            Current = current;
            Clock = clock;
        }

        protected Actor Me => Current.Actor;
        protected Guid Tenant => Current.Actor.TenantId;
        protected WriteContext Write => new(Me.TenantId, Clock.Now, Me.EmpCode, Me.Name);

        protected Task Ensure(string module, string resource, string action, IReadOnlyDictionary<string, string>? attrs = null, CancellationToken ct = default) =>
            Guard.EnsureAsync(new AccessCheck(module, resource, action, attrs), ct);

        protected static T Found<T>(T? item, string what) where T : class => item ?? throw AmsException.NotFound(what);

        protected static void Require(bool condition, string message)
        {
            if (!condition) throw AmsException.Rule(message);
        }
    }

    public static class GuardExtensions
    {
        public static async Task<bool> CanAsync(this IAccessGuard guard, AccessCheck check, CancellationToken ct = default) =>
            (await guard.CanAsync(new[] { check }, ct))[0];

        /// <summary>Keeps only the items the user may see/act on, asking UMS once for the whole list.</summary>
        public static async Task<List<T>> FilterAsync<T>(this IAccessGuard guard, IReadOnlyList<T> items, Func<T, AccessCheck> check, CancellationToken ct = default)
        {
            if (items.Count == 0) return new List<T>();
            var allowed = await guard.CanAsync(items.Select(check).ToList(), ct);
            return items.Where((_, i) => allowed[i]).ToList();
        }

        /// <summary>For each item, which of <paramref name="actions"/> UMS allows (one batch call).</summary>
        public static async Task<List<bool[]>> MatrixAsync<T>(this IAccessGuard guard, IReadOnlyList<T> items, string[] actions, Func<T, string, AccessCheck> check,
            CancellationToken ct = default)
        {
            if (items.Count == 0) return new List<bool[]>();
            var answers = await guard.CanAsync(items.SelectMany(i => actions.Select(a => check(i, a))).ToList(), ct);
            return items.Select((_, i) => answers.Skip(i * actions.Length).Take(actions.Length).ToArray()).ToList();
        }
    }
}
