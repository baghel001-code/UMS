using DomainLayer.Enumerations;
using DomainLayer.Exceptions;
using DomainLayer.Interfaces;

namespace DomainLayer.Entities
{
    /// <summary>
    /// An ABAC policy evaluated after RBAC on every request (see <see cref="PolicyEffect"/>).
    /// Conditions are stored structured, never as free text, so they cannot be used for expression injection.
    /// </summary>
    public sealed class Policy : BaseEntity, IAggregateRoot
    {
        private static readonly string[] AttributeRoots = { "user.", "resource.", "env.", "action" };

        public Guid TenantId { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public PolicyEffect Effect { get; private set; }
        public PolicyTarget Target { get; private set; } = PolicyTarget.Everything;
        public ConditionMatch Match { get; private set; }
        public int Priority { get; private set; }
        public bool IsActive { get; private set; }
        public string CreatedBy { get; private set; } = string.Empty;
        public DateTime CreatedAt { get; private set; }

        private readonly List<PolicyCondition> _conditions = new();
        public IReadOnlyList<PolicyCondition> Conditions => _conditions.AsReadOnly();

        private Policy() { }

        public Policy(Guid id, Guid tenantId, string name, string description, PolicyEffect effect, PolicyTarget target, ConditionMatch match,
            IEnumerable<PolicyCondition> conditions, int priority, bool isActive, string createdBy, DateTime createdAt) : base(id)
        {
            TenantId = tenantId;
            CreatedBy = createdBy;
            CreatedAt = createdAt;
            Update(name, description, effect, target, match, conditions, priority, isActive);
        }

        public void Update(string name, string description, PolicyEffect effect, PolicyTarget target, ConditionMatch match,
            IEnumerable<PolicyCondition> conditions, int priority, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Policy name is required.");
            var list = conditions.ToList();
            // a Deny or Grant without conditions would match every request in its target
            if (effect != PolicyEffect.Restrict && list.Count == 0)
                throw new DomainException($"A {effect} policy needs at least one condition.");
            foreach (var c in list)
            {
                if (!AttributeRoots.Any(r => c.Attribute.StartsWith(r, StringComparison.Ordinal)))
                    throw new DomainException($"Attribute '{c.Attribute}' must start with user., resource. or env.");
                var isSchedule = c.Operator is ConditionOperator.InSchedule or ConditionOperator.NotInSchedule;
                if (!isSchedule && string.IsNullOrWhiteSpace(c.Value))
                    throw new DomainException($"Condition on '{c.Attribute}' needs a value.");
            }

            Name = name.Trim();
            Description = description?.Trim() ?? string.Empty;
            Effect = effect;
            Target = target;
            Match = match;
            Priority = priority;
            IsActive = isActive;
            _conditions.Clear();
            _conditions.AddRange(list);
        }

        public void SetActive(bool isActive) => IsActive = isActive;
    }

    /// <summary>Which requests a policy applies to. App / Module / Resource are codes or "*"; Actions holds action names or "*".</summary>
    public sealed record PolicyTarget(string App, string Module, string Resource, IReadOnlyList<string> Actions, IReadOnlyList<string> Roles)
    {
        public static readonly PolicyTarget Everything = new("*", "*", "*", new[] { "*" }, Array.Empty<string>());
    }

    /// <summary>
    /// attribute (user.X | resource.X | env.X) OPERATOR value. When ValueIsAttribute is true, Value names another attribute,
    /// e.g. resource.store == user.location. For InSchedule the value is a schedule id or code.
    /// </summary>
    public sealed record PolicyCondition(Guid Id, string Attribute, ConditionOperator Operator, string Value, bool ValueIsAttribute);
}
