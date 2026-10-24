using ApplicationLayer.Features.AccessControl.Authorization;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Features.AccessControl.UserAccounts;
using DomainLayer.Authorization;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UMS.AdminPortal.Services
{
    /// <summary>An error returned by the UMS Web API (RFC 7807 problem details).</summary>
    public class ApiException : Exception
    {
        public HttpStatusCode Status { get; }
        public ApiException(HttpStatusCode status, string message) : base(message) => Status = status;
    }

    /// <summary>Signed-in admin for this browser session (one per Blazor circuit).</summary>
    public class AuthSession
    {
        public string? Token { get; private set; }
        public string? FullName { get; private set; }
        public Guid? HomeTenantId { get; private set; }

        public void SignIn(LoginResultDto login)
        {
            Token = login.Token;
            FullName = login.FullName;
            HomeTenantId = login.TenantId;
        }

        public void Restore(string token, string fullName, Guid tenantId)
        {
            Token = token;
            FullName = fullName;
            HomeTenantId = tenantId;
        }

        public void SignOut()
        {
            Token = null;
            FullName = null;
            HomeTenantId = null;
        }
    }

    /// <summary>Tenant chosen in the app bar; every API call is sent for this tenant (X-Tenant-Id).</summary>
    public class TenantState
    {
        public List<TenantDto> Tenants { get; private set; } = new();
        public Guid TenantId { get; private set; }
        public TenantDto? Current => Tenants.FirstOrDefault(t => t.Id == TenantId);
        public event Action? Changed;

        public void SetTenants(List<TenantDto> tenants, Guid? preferred)
        {
            Tenants = tenants;
            var pick = tenants.FirstOrDefault(t => t.Id == preferred) ?? tenants.FirstOrDefault(t => t.Id == TenantId)
                       ?? tenants.FirstOrDefault(t => t.Code == "VMM") ?? tenants.FirstOrDefault();
            TenantId = pick?.Id ?? Guid.Empty;
            Changed?.Invoke();
        }

        public void Select(Guid tenantId)
        {
            if (tenantId == TenantId) return;
            TenantId = tenantId;
            Changed?.Invoke();
        }
    }

    /// <summary>Typed client for the UMS Web API (WebApi/Controllers/V1).</summary>
    public class UmsApiClient
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        private readonly HttpClient _http;
        private readonly AuthSession _session;
        private readonly TenantState _tenant;

        public UmsApiClient(HttpClient http, AuthSession session, TenantState tenant)
        {
            _http = http;
            _session = session;
            _tenant = tenant;
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool withTenant = true)
        {
            using var req = new HttpRequestMessage(method, "api/v1" + path);
            if (_session.Token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
            if (withTenant && _tenant.TenantId != Guid.Empty) req.Headers.Add("X-Tenant-Id", _tenant.TenantId.ToString());
            if (body != null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req);
            }
            catch (HttpRequestException)
            {
                throw new ApiException(HttpStatusCode.ServiceUnavailable, $"Cannot reach the UMS API at {_http.BaseAddress}. Is the WebApi project running?");
            }

            using (res)
            {
                if (!res.IsSuccessStatusCode)
                {
                    var message = $"{(int)res.StatusCode} {res.ReasonPhrase}";
                    try
                    {
                        var problem = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
                        var title = problem.TryGetProperty("title", out var t) ? t.GetString() : null;
                        var detail = problem.TryGetProperty("detail", out var d) ? d.GetString() : null;
                        message = detail != null ? $"{title}: {detail}" : title ?? message;
                    }
                    catch (Exception) { /* not a problem response */ }
                    throw new ApiException(res.StatusCode, message);
                }
                if (typeof(T) == typeof(bool)) return (T)(object)true;
                return (await res.Content.ReadFromJsonAsync<T>(Json))!;
            }
        }

        private Task<T> Get<T>(string path, bool withTenant = true) => SendAsync<T>(HttpMethod.Get, path, null, withTenant);
        private Task<T> Put<T>(string path, object? body = null) => SendAsync<T>(HttpMethod.Put, path, body ?? new { });
        private Task<T> Post<T>(string path, object body) => SendAsync<T>(HttpMethod.Post, path, body);
        private Task<bool> Delete(string path) => SendAsync<bool>(HttpMethod.Delete, path);

        // auth
        public Task<LoginResultDto> LoginAsync(string email, string password) => SendAsync<LoginResultDto>(HttpMethod.Post, "/auth/login", new { email, password }, false);

        // tenants & settings
        public Task<List<TenantDto>> TenantsAsync() => Get<List<TenantDto>>("/tenants", withTenant: false);
        public Task<TenantDto> SaveTenantAsync(TenantDto t) => Put<TenantDto>($"/tenants/{t.Id}", t);
        public Task<SecuritySettings> SettingsAsync() => Get<SecuritySettings>($"/tenants/{_tenant.TenantId}/settings");
        public Task<SecuritySettings> SaveSettingsAsync(SecuritySettings s) => Put<SecuritySettings>($"/tenants/{_tenant.TenantId}/settings", s);

        // applications
        public Task<List<ApplicationDto>> ApplicationsAsync(bool all = false) => Get<List<ApplicationDto>>($"/applications{(all ? "?all=true" : "")}");
        public Task<ApplicationDto> SaveApplicationAsync(ApplicationDto a) => Put<ApplicationDto>($"/applications/{a.Id}", a);
        public Task<bool> DeleteApplicationAsync(Guid id) => Delete($"/applications/{id}");

        // roles
        public Task<List<RoleDto>> RolesAsync() => Get<List<RoleDto>>("/roles");
        public Task<RoleDto> SaveRoleAsync(RoleDto r) => Put<RoleDto>($"/roles/{r.Id}", r);
        public Task<bool> DeleteRoleAsync(Guid id) => Delete($"/roles/{id}");

        // users
        public Task<List<UserAccountDto>> UsersAsync() => Get<List<UserAccountDto>>("/users");
        public Task<UserAccountDto> SaveUserAsync(UserAccountDto u) => Put<UserAccountDto>($"/users/{u.Id}", u);
        public Task<int> BulkUsersAsync(IEnumerable<Guid> ids, UserStatus? status, RoleAssignmentDto? assignRole) =>
            Post<int>("/users/bulk", new { userIds = ids, status, assignRole });
        public Task<ImportResultDto> ImportUsersAsync(List<UserAccountDto> users) => Post<ImportResultDto>("/users/import", users);
        public Task<List<EffectivePermissionDto>> EffectivePermissionsAsync(Guid userId) => Get<List<EffectivePermissionDto>>($"/users/{userId}/effective-permissions");
        public Task<Guid> SetPasswordAsync(Guid userId, string password) => Post<Guid>($"/users/{userId}/password", new { password });

        // policies, schedules, holidays
        public Task<List<PolicyDto>> PoliciesAsync() => Get<List<PolicyDto>>("/policies");
        public Task<PolicyDto> SavePolicyAsync(PolicyDto p) => Put<PolicyDto>($"/policies/{p.Id}", p);
        public Task<bool> DeletePolicyAsync(Guid id) => Delete($"/policies/{id}");
        public Task<List<ScheduleDto>> SchedulesAsync() => Get<List<ScheduleDto>>("/schedules");
        public Task<ScheduleDto> SaveScheduleAsync(ScheduleDto s) => Put<ScheduleDto>($"/schedules/{s.Id}", s);
        public Task<bool> DeleteScheduleAsync(Guid id) => Delete($"/schedules/{id}");
        public Task<List<HolidayDto>> HolidaysAsync() => Get<List<HolidayDto>>("/holidays");
        public Task<HolidayDto> AddHolidayAsync(HolidayDto h) => Post<HolidayDto>("/holidays", h);
        public Task<bool> DeleteHolidayAsync(Guid id) => Delete($"/holidays/{id}");

        // decisions
        public Task<AccessDecision> AuthorizeAsync(AccessRequestDto r) => Post<AccessDecision>("/authorize", r);
        public Task<List<WeekCellDto>> AuthorizeWeekAsync(AccessRequestDto r) => Post<List<WeekCellDto>>("/authorize/week", r);

        // logs
        public Task<List<AuditLogDto>> AuditLogsAsync(int take = 1000) => Get<List<AuditLogDto>>($"/audit-logs?take={take}");
        public Task<List<UserActionLogDto>> UserActionsAsync(int take = 1000) => Get<List<UserActionLogDto>>($"/user-actions?take={take}");
        public Task<List<ExceptionLogDto>> ExceptionsAsync(int take = 1000) => Get<List<ExceptionLogDto>>($"/exceptions?take={take}");
        public Task<ExceptionLogDto> ResolveExceptionAsync(Guid id) => Put<ExceptionLogDto>($"/exceptions/{id}/resolve");
    }
}
