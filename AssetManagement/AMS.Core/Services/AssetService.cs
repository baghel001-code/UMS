using AMS.Core.Entities;
using AMS.Core.Security;
using Microsoft.EntityFrameworkCore;
using static AMS.Core.Security.Perm;

namespace AMS.Core.Services
{
    public class AssetInput
    {
        public string AssetTag { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Serial { get; set; }
        public Guid ModelId { get; set; }
        public Guid StatusLabelId { get; set; }
        public Guid LocationId { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? SupplierId { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public string? OrderNumber { get; set; }
        public int WarrantyMonths { get; set; }
        public bool Requestable { get; set; }
        public string? Notes { get; set; }

        public static AssetInput From(Asset a) => new()
        {
            AssetTag = a.AssetTag, Name = a.Name, Serial = a.Serial, ModelId = a.ModelId, StatusLabelId = a.StatusLabelId, LocationId = a.LocationId,
            DepartmentId = a.DepartmentId, SupplierId = a.SupplierId, PurchaseDate = a.PurchaseDate?.ToDateTime(TimeOnly.MinValue), PurchaseCost = a.PurchaseCost,
            OrderNumber = a.OrderNumber, WarrantyMonths = a.WarrantyMonths, Requestable = a.Requestable, Notes = a.Notes
        };
    }

    public class AssetFilter
    {
        public string? Search { get; set; }
        public Guid? CategoryId { get; set; }
        public Guid? LocationId { get; set; }
        public Guid? StatusLabelId { get; set; }
        /// <summary>null = all, true = checked out, false = in stock.</summary>
        public bool? CheckedOut { get; set; }
    }

    /// <summary>What the user may do with one asset (asked of UMS in one batch for a whole list).</summary>
    public sealed record AssetRow(Asset Asset, bool CanEdit, bool CanDelete, bool CanCheckout, bool CanCheckin, bool CanAudit);

    public sealed record AssetDetails(Asset Asset, List<HistoryEntry> History, List<Maintenance> Maintenances, List<LicenseSeat> Licenses,
        List<ComponentInstall> Components, bool CanEdit, bool CanDelete, bool CanCheckout, bool CanCheckin, bool CanAudit, bool CanMaintain);

    public class AssetService : AmsServiceBase
    {
        private readonly IPeopleDirectory _people;

        public AssetService(IAccessGuard guard, IAmsDbContextFactory factory, IPeopleDirectory people, TimeProvider time) : base(guard, factory, time) =>
            _people = people;

        internal static IQueryable<Asset> WithRefs(IQueryable<Asset> q) => q
            .Include(a => a.Model!).ThenInclude(m => m.Category)
            .Include(a => a.Model!).ThenInclude(m => m.Manufacturer)
            .Include(a => a.StatusLabel).Include(a => a.Location).Include(a => a.Department).Include(a => a.Supplier);

        private static async Task<Asset> LoadAsync(IAmsDbContext db, Guid id, CancellationToken ct) =>
            Found(await WithRefs(db.Assets).FirstOrDefaultAsync(a => a.Id == id, ct), "Asset");

        private static AccessCheck Check(Asset a, string action) => new(Modules.Assets, Resources.Asset, action, ResourceAttrs.For(a));

        /// <summary>Assets the user may view (UMS decides per asset: role scope, "own assets" grant…) with their allowed actions.</summary>
        public async Task<List<AssetRow>> ListAsync(AssetFilter filter, CancellationToken ct = default)
        {
            await using var db = Db();
            var q = WithRefs(db.Assets.AsNoTracking());
            if (filter.CategoryId != null) q = q.Where(a => a.Model!.CategoryId == filter.CategoryId);
            if (filter.LocationId != null) q = q.Where(a => a.LocationId == filter.LocationId);
            if (filter.StatusLabelId != null) q = q.Where(a => a.StatusLabelId == filter.StatusLabelId);
            if (filter.CheckedOut == true) q = q.Where(a => a.AssignedToEmpCode != null);
            if (filter.CheckedOut == false) q = q.Where(a => a.AssignedToEmpCode == null);
            var all = (await q.OrderBy(a => a.AssetTag).ToListAsync(ct))
                .Where(a => string.IsNullOrWhiteSpace(filter.Search) ||
                            $"{a.AssetTag} {a.Name} {a.Serial} {a.Model?.Name} {a.AssignedToName} {a.AssignedToEmpCode}".Contains(filter.Search.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            var visible = await Guard.FilterAsync(all, a => Check(a, Actions.View), ct);
            return await RowsAsync(visible, ct);
        }

        private async Task<List<AssetRow>> RowsAsync(List<Asset> assets, CancellationToken ct)
        {
            if (assets.Count == 0) return new List<AssetRow>();
            string[] actions = { Actions.Edit, Actions.Delete, Actions.Checkout, Actions.Checkin, Actions.Audit };
            var allowed = await Guard.CanAsync(assets.SelectMany(a => actions.Select(x => Check(a, x))).ToList(), ct);
            return assets.Select((a, i) =>
            {
                var o = i * actions.Length;
                return new AssetRow(a, allowed[o], allowed[o + 1], allowed[o + 2] && !a.IsCheckedOut, allowed[o + 3] && a.IsCheckedOut, allowed[o + 4]);
            }).ToList();
        }

        public async Task<AssetDetails> GetAsync(Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var a = await LoadAsync(db, id, ct);
            await Guard.EnsureAsync(Check(a, Actions.View), ct);

            var history = await db.History.AsNoTracking().Where(h => h.ItemType == ItemType.Asset && h.ItemId == id).OrderByDescending(h => h.At).ToListAsync(ct);
            var maint = await db.Maintenances.AsNoTracking().Include(m => m.Supplier).Where(m => m.AssetId == id).OrderByDescending(m => m.StartDate).ToListAsync(ct);
            var seats = await db.LicenseSeats.AsNoTracking().Include(s => s.License).Where(s => s.AssetId == id).ToListAsync(ct);
            var comps = await db.ComponentInstalls.AsNoTracking().Include(c => c.Component).Where(c => c.AssetId == id).ToListAsync(ct);

            var row = (await RowsAsync(new List<Asset> { a }, ct))[0];
            var canMaintain = await Guard.CanAsync(new AccessCheck(Modules.Assets, Resources.Maintenance, Actions.Create, ResourceAttrs.For(a)), ct);
            return new AssetDetails(a, history, maint, seats, comps, row.CanEdit, row.CanDelete, row.CanCheckout, row.CanCheckin, row.CanAudit, canMaintain);
        }

        private static async Task<Asset> ApplyAsync(IAmsDbContext db, Asset a, AssetInput x, CancellationToken ct)
        {
            Require(!string.IsNullOrWhiteSpace(x.AssetTag), "Asset tag is required.");
            Require(!string.IsNullOrWhiteSpace(x.Name), "Name is required.");
            Require(x.PurchaseCost >= 0, "Cost cannot be negative.");
            var tag = x.AssetTag.Trim().ToUpperInvariant();
            Require(!await db.Assets.AnyAsync(o => o.AssetTag == tag && o.Id != a.Id, ct), $"Asset tag {tag} is already used.");

            a.AssetTag = tag;
            a.Name = x.Name.Trim();
            a.Serial = string.IsNullOrWhiteSpace(x.Serial) ? null : x.Serial.Trim();
            a.Model = Found(await db.Models.Include(m => m.Category).Include(m => m.Manufacturer).FirstOrDefaultAsync(m => m.Id == x.ModelId, ct), "Model");
            a.ModelId = a.Model.Id;
            a.StatusLabel = Found(await db.StatusLabels.FirstOrDefaultAsync(s => s.Id == x.StatusLabelId, ct), "Status");
            a.StatusLabelId = a.StatusLabel.Id;
            a.Location = Found(await db.Locations.FirstOrDefaultAsync(l => l.Id == x.LocationId, ct), "Location");
            a.LocationId = a.Location.Id;
            a.Department = x.DepartmentId == null ? null : await db.Departments.FirstOrDefaultAsync(d => d.Id == x.DepartmentId, ct);
            a.DepartmentId = a.Department?.Id;
            a.Supplier = x.SupplierId == null ? null : await db.Suppliers.FirstOrDefaultAsync(s => s.Id == x.SupplierId, ct);
            a.SupplierId = a.Supplier?.Id;
            a.PurchaseDate = x.PurchaseDate == null ? null : DateOnly.FromDateTime(x.PurchaseDate.Value);
            a.PurchaseCost = x.PurchaseCost;
            a.OrderNumber = x.OrderNumber;
            a.WarrantyMonths = Math.Max(0, x.WarrantyMonths);
            a.Requestable = x.Requestable;
            a.Notes = x.Notes;
            return a;
        }

        public async Task<Asset> CreateAsync(AssetInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            var a = await ApplyAsync(db, new Asset { TenantId = Me.TenantId, CreatedAt = Now }, input, ct);
            a.UpdatedAt = Now;
            // the new record's attributes go to UMS: a manager scoped to one location can only create assets there
            await Guard.EnsureAsync(Check(a, Actions.Create), ct);
            db.Assets.Add(a);
            Log(db, ItemType.Asset, a.Id, $"{a.AssetTag} {a.Name}", a.Location?.Code, HistoryAction.Created);
            await db.SaveChangesAsync(ct);
            return a;
        }

        public async Task UpdateAsync(Guid id, AssetInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            var a = await LoadAsync(db, id, ct);
            await Guard.EnsureAsync(Check(a, Actions.Edit), ct);            // allowed to edit it as it is…
            await ApplyAsync(db, a, input, ct);
            await Guard.EnsureAsync(Check(a, Actions.Edit), ct);            // …and as it will be (e.g. moved to another location)
            a.UpdatedAt = Now;
            Log(db, ItemType.Asset, a.Id, $"{a.AssetTag} {a.Name}", a.Location?.Code, HistoryAction.Updated);
            await db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var a = await LoadAsync(db, id, ct);
            await Guard.EnsureAsync(Check(a, Actions.Delete), ct);
            Require(!a.IsCheckedOut, "Check the asset in before deleting it.");
            db.LicenseSeats.RemoveRange(await db.LicenseSeats.Where(s => s.AssetId == id).ToListAsync(ct));
            db.ComponentInstalls.RemoveRange(await db.ComponentInstalls.Where(c => c.AssetId == id).ToListAsync(ct));
            db.Maintenances.RemoveRange(await db.Maintenances.Where(m => m.AssetId == id).ToListAsync(ct));
            db.Requests.RemoveRange(await db.Requests.Where(r => r.AssetId == id).ToListAsync(ct));
            db.Assets.Remove(a);
            Log(db, ItemType.Asset, a.Id, $"{a.AssetTag} {a.Name}", a.Location?.Code, HistoryAction.Deleted);
            await db.SaveChangesAsync(ct);
        }

        public async Task CheckoutAsync(Guid id, string empCode, DateTime? expectedCheckin, string? note, CancellationToken ct = default)
        {
            var person = Found(await _people.FindAsync(empCode, ct), $"Employee {empCode}");
            await using var db = Db();
            var a = await LoadAsync(db, id, ct);
            // UMS sees both the asset and the person it goes to (employment type, department, location)
            await Guard.EnsureAsync(new AccessCheck(Modules.Assets, Resources.Asset, Actions.Checkout, ResourceAttrs.For(a).WithAssignee(person)), ct);
            await CheckoutCoreAsync(db, a, person, expectedCheckin, note, ct);
            await db.SaveChangesAsync(ct);
        }

        internal Task CheckoutCoreAsync(IAmsDbContext db, Asset a, Person person, DateTime? expectedCheckin, string? note, CancellationToken ct)
        {
            Require(!a.IsCheckedOut, $"{a.AssetTag} is already checked out to {a.AssignedToName}.");
            Require(a.StatusLabel?.Type == StatusType.Deployable, $"{a.AssetTag} is '{a.StatusLabel?.Name}', only deployable assets can be checked out.");
            Require(person.Status == "Active", $"{person.FullName} is not an active employee.");
            a.AssignedToEmpCode = person.EmpCode;
            a.AssignedToName = person.FullName;
            a.AssignedAt = Now;
            a.ExpectedCheckin = expectedCheckin == null ? null : DateOnly.FromDateTime(expectedCheckin.Value);
            a.UpdatedAt = Now;
            Log(db, ItemType.Asset, a.Id, $"{a.AssetTag} {a.Name}", a.Location?.Code, HistoryAction.CheckedOut, $"{person.FullName} ({person.EmpCode})", note);
            return Task.CompletedTask;
        }

        public async Task CheckinAsync(Guid id, Guid? statusLabelId, string? note, CancellationToken ct = default)
        {
            await using var db = Db();
            var a = await LoadAsync(db, id, ct);
            await Guard.EnsureAsync(Check(a, Actions.Checkin), ct);
            Require(a.IsCheckedOut, $"{a.AssetTag} is not checked out.");
            var from = $"{a.AssignedToName} ({a.AssignedToEmpCode})";
            a.AssignedToEmpCode = null;
            a.AssignedToName = null;
            a.AssignedAt = null;
            a.ExpectedCheckin = null;
            if (statusLabelId != null)
            {
                a.StatusLabel = Found(await db.StatusLabels.FirstOrDefaultAsync(s => s.Id == statusLabelId, ct), "Status");
                a.StatusLabelId = a.StatusLabel.Id;
            }
            a.UpdatedAt = Now;
            Log(db, ItemType.Asset, a.Id, $"{a.AssetTag} {a.Name}", a.Location?.Code, HistoryAction.CheckedIn, from, note);
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Physical verification: confirms the asset was seen and schedules the next audit.</summary>
        public async Task AuditAsync(Guid id, DateTime? nextAudit, string? note, CancellationToken ct = default)
        {
            await using var db = Db();
            var a = await LoadAsync(db, id, ct);
            await Guard.EnsureAsync(Check(a, Actions.Audit), ct);
            a.LastAuditDate = Today;
            a.NextAuditDate = nextAudit == null ? Today.AddMonths(12) : DateOnly.FromDateTime(nextAudit.Value);
            a.UpdatedAt = Now;
            Log(db, ItemType.Asset, a.Id, $"{a.AssetTag} {a.Name}", a.Location?.Code, HistoryAction.Audited, null, note);
            await db.SaveChangesAsync(ct);
        }

        /// <summary>CSV of the assets the user may view; the export itself is a separate UMS permission (and blocked on unmanaged devices by policy).</summary>
        public async Task<string> ExportCsvAsync(AssetFilter filter, CancellationToken ct = default)
        {
            await Ensure(Modules.Assets, Resources.Asset, Actions.Export, ct: ct);
            var rows = await ListAsync(filter, ct);
            return Csv.Write(new[] { "Asset tag", "Name", "Serial", "Model", "Category", "Status", "Location", "Assigned to", "Purchase date", "Cost", "Warranty expires", "Next audit" },
                rows.Select(r => r.Asset).Select(a => new object?[] { a.AssetTag, a.Name, a.Serial, a.Model?.Name, a.Model?.Category?.Name, a.StatusLabel?.Name,
                    a.Location?.Code, a.AssignedToName, a.PurchaseDate, a.PurchaseCost, a.WarrantyExpires, a.NextAuditDate }));
        }

        /// <summary>Assets to print tag labels for (Print permission, checked per asset).</summary>
        public async Task<List<Asset>> LabelsAsync(AssetFilter filter, CancellationToken ct = default)
        {
            await Ensure(Modules.Assets, Resources.Asset, Actions.Print, ct: ct);
            var rows = await ListAsync(filter, ct);
            return await Guard.FilterAsync(rows.Select(r => r.Asset).ToList(), a => Check(a, Actions.Print), ct);
        }

        public Task<IReadOnlyList<Person>> PeopleAsync(string? search, CancellationToken ct = default) => _people.SearchAsync(search, 30, ct);

        /// <summary>What is issued to me (assets, licence seats, accessories).</summary>
        public async Task<MyItems> MyItemsAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Self, Resources.MyAssets, Actions.View, ct: ct);
            await using var db = Db();
            var me = Me.EmpCode;
            var assets = await WithRefs(db.Assets.AsNoTracking()).Where(a => a.AssignedToEmpCode == me).OrderBy(a => a.AssetTag).ToListAsync(ct);
            var seats = await db.LicenseSeats.AsNoTracking().Include(s => s.License).Where(s => s.AssignedToEmpCode == me).ToListAsync(ct);
            var acc = await db.AccessoryCheckouts.AsNoTracking().Include(c => c.Accessory).Where(c => c.AssignedToEmpCode == me).ToListAsync(ct);
            var cons = await db.ConsumableIssues.AsNoTracking().Include(c => c.Consumable).Where(c => c.IssuedToEmpCode == me).OrderByDescending(c => c.IssuedAt).ToListAsync(ct);
            return new MyItems(assets, seats, acc, cons);
        }
    }

    public sealed record MyItems(List<Asset> Assets, List<LicenseSeat> Licenses, List<AccessoryCheckout> Accessories, List<ConsumableIssue> Consumables);

    public static class Csv
    {
        public static string Write(IEnumerable<string> header, IEnumerable<object?[]> rows)
        {
            static string Cell(object? v)
            {
                var s = v switch
                {
                    null => "",
                    DateOnly d => d.ToString("yyyy-MM-dd"),
                    DateTime t => t.ToString("yyyy-MM-dd HH:mm"),
                    decimal m => ResourceAttrs.Money(m),
                    _ => v.ToString() ?? ""
                };
                // neutralise spreadsheet formulas and quote when needed
                if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
                return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(",", header.Select(Cell)));
            foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Cell)));
            return sb.ToString();
        }
    }
}
