namespace DomainLayer.Entities
{
    /// <summary>
    /// A role given to a user. Scope narrows where the role works (e.g. store = "S045,S046": the role only applies to
    /// resources whose "store" attribute is one of those values). ValidFrom/ValidTo make it temporary.
    /// </summary>
    public sealed record RoleAssignment(
        Guid RoleId,
        IReadOnlyDictionary<string, string> Scope,
        DateOnly? ValidFrom,
        DateOnly? ValidTo,
        string AssignedBy,
        DateOnly AssignedOn)
    {
        public bool IsActiveOn(DateOnly day) =>
            (ValidFrom is null || day >= ValidFrom) && (ValidTo is null || day <= ValidTo);
    }
}
