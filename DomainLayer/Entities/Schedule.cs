using DomainLayer.Enumerations;
using DomainLayer.Exceptions;
using DomainLayer.Interfaces;
using System.Text.RegularExpressions;

namespace DomainLayer.Entities
{
    /// <summary>Named office hours, e.g. "General Office Hours: Mon–Sat 09:30–18:30, closed on holidays".</summary>
    public sealed class Schedule : BaseEntity, IAggregateRoot
    {
        private static readonly Regex TimePattern = new("^([01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.Compiled);

        public Guid TenantId { get; private set; }
        public string Code { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public string Timezone { get; private set; } = "Asia/Kolkata";
        public bool ExcludeHolidays { get; private set; }

        private readonly List<TimeWindow> _windows = new();
        public IReadOnlyList<TimeWindow> Windows => _windows.AsReadOnly();

        private Schedule() { }

        public Schedule(Guid id, Guid tenantId, string code, string name, string timezone, bool excludeHolidays, IEnumerable<TimeWindow> windows) : base(id)
        {
            TenantId = tenantId;
            Update(code, name, timezone, excludeHolidays, windows);
        }

        public void Update(string code, string name, string timezone, bool excludeHolidays, IEnumerable<TimeWindow> windows)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Schedule code is required.");
            if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Schedule name is required.");
            var list = windows.ToList();
            foreach (var w in list)
            {
                if (w.Days.Count == 0) throw new DomainException("Each time window needs at least one day.");
                if (!TimePattern.IsMatch(w.Start) || !TimePattern.IsMatch(w.End)) throw new DomainException("Times must be HH:mm (24h).");
            }

            Code = code.Trim().ToUpperInvariant().Replace(' ', '_');
            Name = name.Trim();
            Timezone = string.IsNullOrWhiteSpace(timezone) ? "Asia/Kolkata" : timezone.Trim();
            ExcludeHolidays = excludeHolidays;
            _windows.Clear();
            _windows.AddRange(list);
        }
    }

    /// <summary>Start/End are "HH:mm". End earlier than Start means an overnight window ending the next day (22:00–06:00).</summary>
    public sealed record TimeWindow(IReadOnlyList<Weekday> Days, string Start, string End);

    public sealed class Holiday : BaseEntity, IAggregateRoot
    {
        public Guid TenantId { get; private set; }
        public DateOnly Date { get; private set; }
        public string Name { get; private set; } = string.Empty;

        private readonly List<string> _regions = new();
        /// <summary>Empty = all regions (all-India holiday).</summary>
        public IReadOnlyList<string> Regions => _regions.AsReadOnly();

        private Holiday() { }

        public Holiday(Guid id, Guid tenantId, DateOnly date, string name, IEnumerable<string> regions) : base(id)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Holiday name is required.");
            TenantId = tenantId;
            Date = date;
            Name = name.Trim();
            _regions.AddRange(regions.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct());
        }

        public bool AppliesTo(string? region) => _regions.Count == 0 || (region != null && _regions.Contains(region));
    }
}
