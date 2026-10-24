using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Mapping;
using DomainLayer.Entities;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Globalization;
using System.Net;

namespace UMS.AdminPortal.Services
{
    /// <summary>Runs API calls for a page: shows the server's message on failure and sends the admin to sign-in on 401.</summary>
    public class Ui
    {
        private readonly ISnackbar _snackbar;
        private readonly NavigationManager _nav;
        private readonly AuthSession _session;

        public Ui(ISnackbar snackbar, NavigationManager nav, AuthSession session)
        {
            _snackbar = snackbar;
            _nav = nav;
            _session = session;
        }

        public void Ok(string message) => _snackbar.Add(message, Severity.Success);
        public void Error(string message) => _snackbar.Add(message, Severity.Error);

        public async Task<T?> Run<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (ApiException ex) when (ex.Status == HttpStatusCode.Unauthorized)
            {
                // the session ended (expired, idle, signed in elsewhere, revoked): the sign-in page says why
                _session.SignOut(ex.SessionEnded);
                _nav.NavigateTo(ex.SessionEnded == null ? "/login" : $"/login?reason={ex.SessionEnded}");
                return default;
            }
            catch (ApiException ex)
            {
                Error(ex.Message);
                return default;
            }
        }

        public async Task<bool> Run(Func<Task> call) => await Run(async () => { await call(); return true; });
    }

    /// <summary>The browser's address and user agent, captured on the first HTTP request of the circuit (sent to UMS on sign-in).</summary>
    public class ClientInfo
    {
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
    }

    public static class Fmt
    {
        private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
        public static string DateTime(DateTime? d) => d is null ? "—" : d.Value.ToString("dd MMM yyyy, hh:mm tt", India);
        public static string Date(DateOnly? d) => d is null ? "—" : d.Value.ToString("dd MMM yyyy", India);
        public static string Number(int n) => n.ToString("N0", India);
        public static Color StatusColor(string status) => status switch
        {
            "Active" or "Allow" => Color.Success,
            "Inactive" => Color.Warning,
            "Locked" or "Deny" => Color.Error,
            _ => Color.Default
        };
    }

    /// <summary>Builds domain objects from DTOs so pages can reuse domain logic (schedule grid, rule preview).</summary>
    public static class DomainPreview
    {
        public static Schedule ToSchedule(this ScheduleDto s) =>
            new(s.Id, s.TenantId, s.Code, s.Name, s.Timezone, s.ExcludeHolidays, s.Windows.Select(w => new TimeWindow(w.Days, w.Start, w.End)));

        /// <summary>Rule text for a policy being edited, or the domain rule it breaks (e.g. "A Deny policy needs at least one condition").</summary>
        public static string RuleText(PolicyDto p, IEnumerable<ScheduleDto> schedules)
        {
            try
            {
                var policy = new Policy(p.Id, p.TenantId, string.IsNullOrWhiteSpace(p.Name) ? "preview" : p.Name, p.Description, p.Effect,
                    p.Target.ToTarget(), p.Match, p.Conditions.ToConditions(), p.Priority, p.IsActive, "", default);
                return DomainLayer.Services.ConditionEvaluator.Expression(policy, schedules.Select(s => s.ToSchedule()).ToList());
            }
            catch (Exception ex) when (ex is DomainLayer.Exceptions.DomainException or ArgumentException)
            {
                return $"⚠ {ex.Message}";
            }
        }
    }
}
