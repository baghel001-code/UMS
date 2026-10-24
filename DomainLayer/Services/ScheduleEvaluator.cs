using DomainLayer.Entities;
using DomainLayer.Enumerations;

namespace DomainLayer.Services
{
    public static class ScheduleEvaluator
    {
        public static Weekday WeekdayOf(DateTime at) => at.DayOfWeek switch
        {
            DayOfWeek.Monday => Weekday.Mon,
            DayOfWeek.Tuesday => Weekday.Tue,
            DayOfWeek.Wednesday => Weekday.Wed,
            DayOfWeek.Thursday => Weekday.Thu,
            DayOfWeek.Friday => Weekday.Fri,
            DayOfWeek.Saturday => Weekday.Sat,
            _ => Weekday.Sun
        };

        public static Holiday? FindHoliday(IEnumerable<Holiday> holidays, Guid tenantId, DateOnly date, string? region) =>
            holidays.FirstOrDefault(h => h.TenantId == tenantId && h.Date == date && h.AppliesTo(region));

        /// <summary>Whether <paramref name="at"/> (schedule-local time) falls in one of the schedule's windows. Handles overnight windows.</summary>
        public static bool IsInSchedule(Schedule schedule, DateTime at, IEnumerable<Holiday> holidays, string? region)
        {
            if (schedule.ExcludeHolidays && FindHoliday(holidays, schedule.TenantId, DateOnly.FromDateTime(at), region) != null)
                return false;

            var time = at.ToString("HH:mm");
            var today = WeekdayOf(at);
            var yesterday = WeekdayOf(at.AddDays(-1));
            return schedule.Windows.Any(w =>
            {
                if (string.CompareOrdinal(w.Start, w.End) <= 0)
                    return w.Days.Contains(today) && string.CompareOrdinal(time, w.Start) >= 0 && string.CompareOrdinal(time, w.End) < 0;
                // overnight: the part after Start belongs to the start day, the part before End to the next day
                return (w.Days.Contains(today) && string.CompareOrdinal(time, w.Start) >= 0)
                    || (w.Days.Contains(yesterday) && string.CompareOrdinal(time, w.End) < 0);
            });
        }
    }
}
