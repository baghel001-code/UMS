using AMS.Application.Interfaces;
using AMS.Domain.Base;
using AMS.Domain.Enums;
using AMS.Domain.Responses;
using AMS.Infrastructure.Api;
using AMS.Portal.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using System.Globalization;

namespace AMS.Portal.Services
{
    /// <summary>
    /// What the signed-in user may do at type level (asked of UMS through the API once per sign-in / refresh). Only shapes the UI:
    /// the API checks every action again against UMS with the record's attributes.
    /// </summary>
    public sealed class AppState
    {
        private readonly IAPICall _api;
        public CapabilitiesRes Caps { get; private set; } = new();
        public event Action? Changed;

        public AppState(IAPICall api) => _api = api;

        public async Task<CapabilitiesRes> RefreshAsync()
        {
            var caps = await _api.CallingAPI<CapabilitiesRes, object>(AmsApiNames.GET_CAPABILITIES, new { });
            if (caps.IsSuccess) Caps = caps;
            Changed?.Invoke();
            return caps;
        }

        public bool Can(string module, string resource, string action) => Caps.Can(module, resource, action);
    }

    /// <summary>
    /// Calls the AMS API for a page and turns refusals into friendly messages: 403 shows UMS's reason, 401 (session ended - signed in
    /// elsewhere, idle, revoked) signs the user out with that reason, anything else shows the API's message.
    /// </summary>
    public sealed class Ui
    {
        private readonly IAPICall _api;
        private readonly ISnackbar _snackbar;
        private readonly NavigationManager _nav;
        private readonly IJSRuntime _js;
        private readonly IDialogService _dialogs;
        private readonly SessionEndState _ended;

        public Ui(IAPICall api, ISnackbar snackbar, NavigationManager nav, IJSRuntime js, IDialogService dialogs, SessionEndState ended)
        {
            _api = api;
            _snackbar = snackbar;
            _nav = nav;
            _js = js;
            _dialogs = dialogs;
            _ended = ended;
        }

        public void Ok(string message) => _snackbar.Add(message, Severity.Success);
        public void Error(string message) => _snackbar.Add(message, Severity.Error);
        public void Denied(string reason) => _snackbar.Add($"Not allowed by UMS — {reason}", Severity.Warning, o => o.Icon = Icons.Material.Filled.GppBad);

        /// <summary>Calls an API operation; returns the response when it succeeded, otherwise shows why and returns null.</summary>
        /// <param name="denied">Receives UMS's reason for a 403 instead of the default warning (pages show a "no access" card).</param>
        public async Task<TRes?> Call<TRes, TReq>(string apiName, TReq request, Action<string>? denied = null) where TRes : BaseResponse, new()
        {
            var res = await _api.CallingAPI<TRes, TReq>(apiName, request);
            if (res.IsSuccess) return res;
            Handle(res, denied);
            return null;
        }

        public Task<TRes?> Get<TRes>(string apiName, Action<string>? denied = null) where TRes : BaseResponse, new() => Call<TRes, object>(apiName, new { }, denied);

        /// <summary>For writes: true on success (with an optional success message).</summary>
        public async Task<bool> Do<TReq>(string apiName, TReq request, string? success = null)
        {
            var res = await Call<InUpRes, TReq>(apiName, request);
            if (res == null) return false;
            Ok(success ?? res.responseMessage ?? "Done");
            return true;
        }

        public void Handle(BaseResponse res, Action<string>? denied = null)
        {
            switch (res.responseCode)
            {
                case ResponseCodes.Unauthorized:
                    SignOut(_ended.Reason ?? "expired");
                    break;
                case ResponseCodes.Forbidden when denied != null:
                    denied(res.responseMessage ?? "");
                    break;
                case ResponseCodes.Forbidden:
                    Denied(res.responseMessage ?? "");
                    break;
                default:
                    Error(res.responseMessage ?? "Something went wrong.");
                    break;
            }
        }

        /// <summary>Signs out with a real HTTP POST (clears the cookie, ends the UMS session) via the logout page.</summary>
        public void SignOut(string reason = "signedout") => _nav.NavigateTo($"{AuthConstants.LogoutPagePath}?reason={Uri.EscapeDataString(reason)}");

        public async Task DownloadAsync(FileRes file) =>
            await _js.InvokeVoidAsync("ams.download", file.FileName, $"{file.ContentType};charset=utf-8", "﻿" + file.Content);

        public async Task<bool> ConfirmAsync(string title, string text, string yes = "Yes") =>
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
        public static string Date(DateTime? d) => d is null ? "—" : d.Value.ToString("dd MMM yyyy", India);
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
