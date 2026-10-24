using AMS.Core.Entities;
using AMS.Core.Security;
using AMS.Core.Services;
using AMS.Infrastructure.Ums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using MudBlazor;
using System.Globalization;

namespace AMS.Web.Services
{
    /// <summary>
    /// What the signed-in user may do at type level (asked of UMS once per sign-in / refresh) — only used to shape the UI.
    /// The services check every action again against UMS with the record's attributes.
    /// </summary>
    public class AppState
    {
        private readonly ReportService _reports;
        public Capabilities Caps { get; private set; } = Capabilities.None;
        public event Action? Changed;

        public AppState(ReportService reports) => _reports = reports;

        public async Task RefreshAsync()
        {
            Caps = await _reports.CapabilitiesAsync();
            Changed?.Invoke();
        }

        public void Clear()
        {
            Caps = Capabilities.None;
            Changed?.Invoke();
        }

        public bool Can(string module, string resource, string action) => Caps.Can(module, resource, action);
    }

    /// <summary>Runs service calls for a page and turns UMS denials, rule violations and expired sessions into friendly messages.</summary>
    public class Ui
    {
        public const string SessionKey = "ams-session";
        private readonly ISnackbar _snackbar;
        private readonly NavigationManager _nav;
        private readonly UmsSession _session;
        private readonly IJSRuntime _js;
        private readonly IDialogService _dialogs;
        private readonly ProtectedSessionStorage _storage;

        public Ui(ISnackbar snackbar, NavigationManager nav, UmsSession session, IJSRuntime js, IDialogService dialogs, ProtectedSessionStorage storage)
        {
            _snackbar = snackbar;
            _nav = nav;
            _session = session;
            _js = js;
            _dialogs = dialogs;
            _storage = storage;
        }

        public void Ok(string message) => _snackbar.Add(message, Severity.Success);
        public void Error(string message) => _snackbar.Add(message, Severity.Error);
        public void Denied(string reason) => _snackbar.Add($"Not allowed by UMS — {reason}", Severity.Warning, o => o.Icon = Icons.Material.Filled.GppBad);

        /// <summary>Runs a call; on failure shows why and returns default. <paramref name="denied"/> receives the UMS reason instead of a snackbar.</summary>
        public async Task<T?> Run<T>(Func<Task<T>> call, Action<string>? denied = null)
        {
            try
            {
                return await call();
            }
            catch (AccessDeniedException ex)
            {
                if (denied != null) denied(ex.Message);
                else Denied(ex.Message);
            }
            catch (AmsValidationException ex)
            {
                Error(ex.Message);
            }
            catch (UmsException ex) when (ex.IsUnauthorized)
            {
                await SignOutAsync();
                _snackbar.Add(ex.Message, Severity.Info);
            }
            catch (UmsException ex)
            {
                Error(ex.Message);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                Error($"Could not save: {ex.InnerException?.Message ?? ex.Message}");
            }
            return default;
        }

        public async Task<bool> Run(Func<Task> call, string? success = null, Action<string>? denied = null)
        {
            var ok = await Run(async () => { await call(); return true; }, denied);
            if (ok && success != null) Ok(success);
            return ok;
        }

        public async Task SignOutAsync()
        {
            _session.SignOut();
            try { await _storage.DeleteAsync(SessionKey); } catch (Exception) { /* circuit closing */ }
            _nav.NavigateTo("/login");
        }

        public async Task DownloadCsvAsync(string name, string csv) =>
            await _js.InvokeVoidAsync("ams.download", $"{name}-{DateTime.Now:yyyyMMdd-HHmm}.csv", "text/csv;charset=utf-8", "﻿" + csv);

        public async Task<bool> ConfirmAsync(string title, string text, string yes = "Yes", Color color = Color.Error) =>
            await _dialogs.ShowMessageBox(title, text, yesText: yes, cancelText: "Cancel") == true;

        public async Task<T?> DialogAsync<TDialog, T>(string title, DialogParameters<TDialog> parameters, MaxWidth width = MaxWidth.Small) where TDialog : IComponent
        {
            var dialog = await _dialogs.ShowAsync<TDialog>(title, parameters, new DialogOptions { MaxWidth = width, FullWidth = true, CloseButton = true });
            var result = await dialog.Result;
            return result is { Canceled: false, Data: T data } ? data : default;
        }
    }

    public static class Fmt
    {
        private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
        public static string DateTime(DateTime? d) => d is null ? "—" : d.Value.ToString("dd MMM yyyy, hh:mm tt", India);
        public static string Date(DateOnly? d) => d is null ? "—" : d.Value.ToString("dd MMM yyyy", India);
        public static string Number(int n) => n.ToString("N0", India);
        public static string Money(decimal v) => "₹" + v.ToString("N0", India);
        public static string Compact(decimal v) => v >= 10_000_000 ? $"₹{v / 10_000_000:0.##} Cr" : v >= 100_000 ? $"₹{v / 100_000:0.##} L" : Money(v);

        public static Color StatusColor(StatusType? t) => t switch
        {
            StatusType.Deployable => Color.Success,
            StatusType.Pending => Color.Warning,
            StatusType.Undeployable => Color.Error,
            _ => Color.Default
        };

        public static Color RequestColor(RequestStatus s) => s switch
        {
            RequestStatus.Pending => Color.Warning,
            RequestStatus.Approved => Color.Success,
            RequestStatus.Rejected => Color.Error,
            _ => Color.Default
        };

        public static string Action(HistoryAction a) => a switch
        {
            HistoryAction.CheckedOut => "Checked out",
            HistoryAction.CheckedIn => "Checked in",
            _ => a.ToString()
        };

        public static string Icon(HistoryAction a) => a switch
        {
            HistoryAction.Created => Icons.Material.Filled.AddCircle,
            HistoryAction.Updated => Icons.Material.Filled.Edit,
            HistoryAction.Deleted => Icons.Material.Filled.Delete,
            HistoryAction.CheckedOut => Icons.Material.Filled.Logout,
            HistoryAction.CheckedIn => Icons.Material.Filled.Login,
            HistoryAction.Audited => Icons.Material.Filled.FactCheck,
            HistoryAction.Maintenance => Icons.Material.Filled.Build,
            HistoryAction.Approved => Icons.Material.Filled.ThumbUp,
            HistoryAction.Rejected => Icons.Material.Filled.ThumbDown,
            _ => Icons.Material.Filled.Bolt
        };
    }
}
