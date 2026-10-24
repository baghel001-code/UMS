using AMS.Core.Entities;
using AMS.Core.Security;
using AMS.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace AMS.Tests
{
    // AMS holds no access rules: every operation asks the guard (UMS) and acts only on Allow, passing the record's attributes.
    public class ServiceAuthorizationTests
    {
        private static async Task<Asset> FreeLaptopAsync(Harness h, string location = "HO-GGN")
        {
            await using var db = h.Db();
            return await db.Assets.Include(a => a.Location).Include(a => a.StatusLabel).Include(a => a.Model!).ThenInclude(m => m.Category)
                .FirstAsync(a => a.AssignedToEmpCode == null && a.Location!.Code == location && a.StatusLabel!.Type == StatusType.Deployable && a.Model!.Category!.Name == "Laptops");
        }

        [Fact]
        public async Task Denied_operations_throw_and_change_nothing()
        {
            await using var h = await Harness.CreateAsync();
            var asset = await FreeLaptopAsync(h);
            int before;
            await using (var db = h.Db()) before = await db.History.CountAsync();
            h.Guard.Rule = _ => false;

            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.CheckoutAsync(asset.Id, "VMM10002", null, null));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.DeleteAsync(asset.Id));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.AuditAsync(asset.Id, null, null));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.UpdateAsync(asset.Id, AssetInput.From(asset)));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.GetAsync(asset.Id));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.ExportCsvAsync(new AssetFilter()));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.MyItemsAsync());
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Requests.CreateAsync(asset.Id, "need it"));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Maintenance.CreateAsync(new MaintenanceInput { AssetId = asset.Id, Title = "x" }));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Lookups.ListAsync(LookupKind.Location));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Lookups.SaveAsync(new LookupItem { Kind = LookupKind.Department, Name = "Legal" }));
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Reports.DashboardAsync());
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Reports.AssetReportAsync());
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Reports.ActivityAsync(null, null, null, null));

            Assert.Empty(await h.Assets.ListAsync(new AssetFilter()));
            Assert.Empty(await h.Licenses.ListAsync(null));
            Assert.Empty(await h.Inventory.ListAsync(StockKind.Accessory, null));
            Assert.Empty(await h.Requests.ListAsync(null));
            Assert.Empty(await h.Maintenance.ListAsync(false));
            Assert.False((await h.Reports.CapabilitiesAsync()).Any);

            await using var after = h.Db();
            Assert.Equal(before, await after.History.CountAsync());
            Assert.Null((await after.Assets.FirstAsync(a => a.Id == asset.Id)).AssignedToEmpCode);
        }

        [Fact]
        public async Task Lists_are_filtered_record_by_record_with_resource_attributes()
        {
            await using var h = await Harness.CreateAsync();
            // what UMS does for a manager scoped to location HO-GGN
            h.Guard.Rule = c => !c.Attributes!.TryGetValue("location", out var loc) || loc == "HO-GGN";
            var rows = await h.Assets.ListAsync(new AssetFilter());
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.Equal("HO-GGN", r.Asset.Location!.Code));
            await using var db = h.Db();
            Assert.True(await db.Assets.CountAsync() > rows.Count);
            Assert.Contains(h.Guard.Asked, c => c.Key == "AMS.ASSETS.ASSET.VIEW" && c.Attributes!.ContainsKey("cost") && c.Attributes.ContainsKey("category"));
        }

        [Fact]
        public async Task Checkout_tells_UMS_who_receives_the_asset()
        {
            await using var h = await Harness.CreateAsync();
            var asset = await FreeLaptopAsync(h);
            // e.g. "high-value assets are not issued to contract staff"
            h.Guard.Rule = c => !(c.Action == Perm.Actions.Checkout && c.Attributes!.GetValueOrDefault("assigneeEmploymentType") == "Contract");

            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.CheckoutAsync(asset.Id, "VMM10007", null, null));
            await h.Assets.CheckoutAsync(asset.Id, "VMM10002", new DateTime(2026, 12, 31), "new joiner kit");

            var check = h.Guard.Ensured.Last();
            Assert.Equal("AMS.ASSETS.ASSET.CHECKOUT", check.Key);
            Assert.Equal("Permanent", check.Attributes!["assigneeEmploymentType"]);
            Assert.Equal("HO-GGN", check.Attributes["location"]);

            await using var db = h.Db();
            var saved = await db.Assets.FirstAsync(a => a.Id == asset.Id);
            Assert.Equal("VMM10002", saved.AssignedToEmpCode);
            Assert.Equal(new DateOnly(2026, 12, 31), saved.ExpectedCheckin);
            Assert.True(await db.History.AnyAsync(x => x.ItemId == asset.Id && x.Action == HistoryAction.CheckedOut && x.Note == "new joiner kit"));
            await Assert.ThrowsAsync<AmsValidationException>(() => h.Assets.CheckoutAsync(asset.Id, "VMM10006", null, null));
        }

        [Fact]
        public async Task Editing_is_checked_before_and_after_the_change()
        {
            await using var h = await Harness.CreateAsync();
            var asset = await FreeLaptopAsync(h);
            h.Guard.Rule = c => !c.Attributes!.TryGetValue("location", out var loc) || loc == "HO-GGN";
            Guid store;
            await using (var db = h.Db()) store = (await db.Locations.FirstAsync(l => l.Code == "S045")).Id;

            var input = AssetInput.From(asset);
            input.LocationId = store;     // moving it out of the manager's scope
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Assets.UpdateAsync(asset.Id, input));
            await using var check = h.Db();
            Assert.Equal("HO-GGN", (await check.Assets.Include(a => a.Location).FirstAsync(a => a.Id == asset.Id)).Location!.Code);
        }

        [Fact]
        public async Task Approving_a_request_needs_approve_and_checkout_and_issues_the_asset()
        {
            await using var h = await Harness.CreateAsync();
            var pending = (await h.Requests.ListAsync(RequestStatus.Pending)).Select(r => r.Request).ToList();
            var own = pending.First(r => r.RequestedByEmpCode == "VMM10010");
            var other = pending.First(r => r.RequestedByEmpCode == "VMM10002");

            // what the UMS "no self-approval" policy does
            h.Guard.Rule = c => !(c.Action == Perm.Actions.Approve && c.Attributes!.GetValueOrDefault("requestedByEmpCode") == h.Guard.Actor.EmpCode);
            await Assert.ThrowsAsync<AccessDeniedException>(() => h.Requests.ApproveAsync(own.Id, null));
            await h.Requests.ApproveAsync(other.Id, "ok");

            Assert.Contains(h.Guard.Ensured, c => c.Key == "AMS.ASSETS.REQUEST.APPROVE" && c.Attributes!["requestedByEmpCode"] == "VMM10002");
            Assert.Contains(h.Guard.Ensured, c => c.Key == "AMS.ASSETS.ASSET.CHECKOUT" && c.Attributes!["assigneeEmpCode"] == "VMM10002");
            await using var db = h.Db();
            Assert.Equal(RequestStatus.Approved, (await db.Requests.FirstAsync(r => r.Id == other.Id)).Status);
            Assert.Equal("VMM10002", (await db.Assets.FirstAsync(a => a.Id == other.AssetId)).AssignedToEmpCode);
        }

        [Fact]
        public async Task Consumables_are_used_up_and_components_go_into_assets()
        {
            await using var h = await Harness.CreateAsync();
            var toner = (await h.Inventory.ListAsync(StockKind.Consumable, "toner"))[0];
            await h.Inventory.CheckoutAsync(StockKind.Consumable, toner.Item.Id, "VMM10006", null, 2, null);
            var after = (await h.Inventory.ListAsync(StockKind.Consumable, "toner"))[0];
            Assert.Equal(toner.Item.Quantity - 2, after.Item.Quantity);
            await Assert.ThrowsAsync<AmsValidationException>(() => h.Inventory.CheckoutAsync(StockKind.Consumable, toner.Item.Id, "VMM10006", null, 999, null));

            var ram = (await h.Inventory.ListAsync(StockKind.Component, "DDR5"))[0];
            var laptop = await FreeLaptopAsync(h);
            await h.Inventory.CheckoutAsync(StockKind.Component, ram.Item.Id, null, laptop.Id, 1, null);
            var holders = await h.Inventory.HoldersAsync(StockKind.Component, ram.Item.Id);
            Assert.Equal(ram.InUse + 1, holders.Sum(x => x.Quantity));
            Assert.Contains(h.Guard.Ensured, c => c.Key == "AMS.COMPONENTS.COMPONENT.CHECKOUT" && c.Attributes!["assetTag"] == laptop.AssetTag);
        }

        [Fact]
        public async Task Product_keys_are_hidden_from_users_who_cannot_edit_the_licence()
        {
            await using var h = await Harness.CreateAsync();
            h.Guard.Rule = c => c.Action != Perm.Actions.Edit;
            Assert.All(await h.Licenses.ListAsync(null), r => Assert.Null(r.License.ProductKey));
            h.Guard.Rule = _ => true;
            Assert.All(await h.Licenses.ListAsync(null), r => Assert.NotNull(r.License.ProductKey));
        }

        [Fact]
        public async Task Data_of_other_tenants_is_invisible()
        {
            await using var h = await Harness.CreateAsync();
            await using var other = h.Db(Guid.NewGuid());
            Assert.Equal(0, await other.Assets.CountAsync());
            Assert.Equal(0, await other.Locations.CountAsync());
            await using var mine = h.Db();
            Assert.True(await mine.Assets.CountAsync() > 50);
        }

        [Fact]
        public async Task Csv_export_neutralises_formulas()
        {
            var csv = Csv.Write(new[] { "a", "b" }, new[] { new object?[] { "=HYPERLINK(\"x\")", "plain, text" } });
            Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
            Assert.Contains("\"plain, text\"", csv);
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Depreciation_is_straight_line_over_the_category_life()
        {
            var a = new Asset { PurchaseCost = 36000, PurchaseDate = new DateOnly(2025, 10, 1), Model = new AssetModel { Category = new Category { DepreciationMonths = 36 } } };
            Assert.Equal(24000, ReportService.CurrentValue(a, new DateOnly(2026, 10, 24), out var months));
            Assert.Equal(12, months);
            Assert.Equal(0, ReportService.CurrentValue(a, new DateOnly(2030, 1, 1), out _));
            await Task.CompletedTask;
        }
    }
}
