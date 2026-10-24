namespace ApplicationLayer.Common
{
    /// <summary>Wall-clock time in the business time zone (IST by default). Office-hour schedules are written in this time.</summary>
    public static class LocalClock
    {
        public const string DefaultTimeZone = "Asia/Kolkata";

        public static DateTime Now(TimeProvider time, string timeZone = DefaultTimeZone)
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
            catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
            var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), tz).DateTime;
            return new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
        }

        public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(Now(time));
    }
}
