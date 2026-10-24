using Microsoft.AspNetCore.Components;
using UMS.AdminPortal.Services;

namespace UMS.AdminPortal.Components.Shared
{
    /// <summary>Base for pages whose data belongs to the tenant selected in the app bar; reloads when it changes.</summary>
    public abstract class TenantPage : ComponentBase
    {
        [CascadingParameter(Name = "TenantId")] public Guid TenantId { get; set; }
        [Inject] protected UmsApiClient Api { get; set; } = default!;
        [Inject] protected Ui Ui { get; set; } = default!;

        protected bool Loading { get; private set; } = true;
        private Guid _loadedFor;

        protected override async Task OnParametersSetAsync()
        {
            if (TenantId == Guid.Empty || TenantId == _loadedFor) return;
            _loadedFor = TenantId;
            await ReloadAsync();
        }

        protected async Task ReloadAsync()
        {
            Loading = true;
            await Ui.Run(LoadAsync);
            Loading = false;
            StateHasChanged();
        }

        protected abstract Task LoadAsync();
    }
}
