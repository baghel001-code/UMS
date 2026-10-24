using AMS.Core.Entities;
using AMS.Core.Security;
using Microsoft.EntityFrameworkCore;
using static AMS.Core.Security.Perm;

namespace AMS.Core.Services
{
    public class LicenseInput
    {
        public string Name { get; set; } = "";
        public string? ProductKey { get; set; }
        public int Seats { get; set; } = 1;
        public Guid? ManufacturerId { get; set; }
        public Guid? CategoryId { get; set; }
        public Guid? SupplierId { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string? Notes { get; set; }

        public static LicenseInput From(License l) => new()
        {
            Name = l.Name, ProductKey = l.ProductKey, Seats = l.Seats, ManufacturerId = l.ManufacturerId, CategoryId = l.CategoryId, SupplierId = l.SupplierId,
            PurchaseDate = l.PurchaseDate?.ToDateTime(TimeOnly.MinValue), PurchaseCost = l.PurchaseCost, ExpirationDate = l.ExpirationDate?.ToDateTime(TimeOnly.MinValue),
            Notes = l.Notes
        };
    }

    public sealed record LicenseRow(License License, int Used, bool CanEdit, bool CanDelete, bool CanCheckout, bool CanCheckin, bool CanSeeKey)
    {
        public int Free => License.Seats - Used;
    }

    /// <summary>Software licences and their seats (given to a person and/or installed on an asset).</summary>
    public class LicenseService : AmsServiceBase
    {
        private readonly IPeopleDirectory _people;

        public LicenseService(IAccessGuard guard, IAmsDbContextFactory factory, IPeopleDirectory people, TimeProvider time) : base(guard, factory, time) =>
            _people = people;

        private static AccessCheck Check(License l, string action) => new(Modules.Licenses, Resources.License, action, ResourceAttrs.For(l));

        private static IQueryable<License> WithRefs(IQueryable<License> q) =>
            q.Include(l => l.Manufacturer).Include(l => l.Category).Include(l => l.Supplier).Include(l => l.SeatAssignments).ThenInclude(s => s.Asset);

        public async Task<List<LicenseRow>> ListAsync(string? search, CancellationToken ct = default)
        {
            await using var db = Db();
            var all = (await WithRefs(db.Licenses.AsNoTracking()).OrderBy(l => l.Name).ToListAsync(ct))
                .Where(l => string.IsNullOrWhiteSpace(search) || $"{l.Name} {l.Manufacturer?.Name}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            var visible = await Guard.FilterAsync(all, l => Check(l, Actions.View), ct);
            if (visible.Count == 0) return new List<LicenseRow>();
            // product keys are shown only to those allowed to edit the licence
            string[] actions = { Actions.Edit, Actions.Delete, Actions.Checkout, Actions.Checkin };
            var allowed = await Guard.CanAsync(visible.SelectMany(l => actions.Select(a => Check(l, a))).ToList(), ct);
            return visible.Select((l, i) =>
            {
                var o = i * actions.Length;
                var used = l.SeatAssignments.Count;
                if (!allowed[o]) l.ProductKey = null;
                return new LicenseRow(l, used, allowed[o], allowed[o + 1], allowed[o + 2] && used < l.Seats, allowed[o + 3] && used > 0, allowed[o]);
            }).ToList();
        }

        private void Apply(License l, LicenseInput x, int used)
        {
            Require(!string.IsNullOrWhiteSpace(x.Name), "Name is required.");
            Require(x.Seats >= 1, "A licence needs at least one seat.");
            Require(x.Seats >= used, $"{used} seats are in use; check some in before reducing seats to {x.Seats}.");
            Require(x.PurchaseCost >= 0, "Cost cannot be negative.");
            l.Name = x.Name.Trim();
            l.ProductKey = string.IsNullOrWhiteSpace(x.ProductKey) ? null : x.ProductKey.Trim();
            l.Seats = x.Seats;
            l.ManufacturerId = x.ManufacturerId;
            l.CategoryId = x.CategoryId;
            l.SupplierId = x.SupplierId;
            l.PurchaseDate = x.PurchaseDate == null ? null : DateOnly.FromDateTime(x.PurchaseDate.Value);
            l.PurchaseCost = x.PurchaseCost;
            l.ExpirationDate = x.ExpirationDate == null ? null : DateOnly.FromDateTime(x.ExpirationDate.Value);
            l.Notes = x.Notes;
        }

        private static async Task LoadRefsAsync(IAmsDbContext db, License l, CancellationToken ct)
        {
            l.Category = l.CategoryId == null ? null : await db.Categories.FirstOrDefaultAsync(c => c.Id == l.CategoryId, ct);
            l.Manufacturer = l.ManufacturerId == null ? null : await db.Manufacturers.FirstOrDefaultAsync(m => m.Id == l.ManufacturerId, ct);
        }

        public async Task CreateAsync(LicenseInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            var l = new License { TenantId = Me.TenantId };
            Apply(l, input, 0);
            await LoadRefsAsync(db, l, ct);
            await Guard.EnsureAsync(Check(l, Actions.Create), ct);
            db.Licenses.Add(l);
            Log(db, ItemType.License, l.Id, l.Name, null, HistoryAction.Created, $"{l.Seats} seats");
            await db.SaveChangesAsync(ct);
        }

        public async Task UpdateAsync(Guid id, LicenseInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            var l = Found(await WithRefs(db.Licenses).FirstOrDefaultAsync(x => x.Id == id, ct), "Licence");
            await Guard.EnsureAsync(Check(l, Actions.Edit), ct);
            Apply(l, input, l.SeatAssignments.Count);
            await LoadRefsAsync(db, l, ct);
            await Guard.EnsureAsync(Check(l, Actions.Edit), ct);
            Log(db, ItemType.License, l.Id, l.Name, null, HistoryAction.Updated);
            await db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var l = Found(await WithRefs(db.Licenses).FirstOrDefaultAsync(x => x.Id == id, ct), "Licence");
            await Guard.EnsureAsync(Check(l, Actions.Delete), ct);
            Require(l.SeatAssignments.Count == 0, "Check in all seats before deleting the licence.");
            db.Licenses.Remove(l);
            Log(db, ItemType.License, l.Id, l.Name, null, HistoryAction.Deleted);
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Gives one seat to a person and/or installs it on an asset.</summary>
        public async Task CheckoutAsync(Guid id, string? empCode, Guid? assetId, string? note, CancellationToken ct = default)
        {
            Require(!string.IsNullOrWhiteSpace(empCode) || assetId != null, "Choose a person or an asset.");
            var person = string.IsNullOrWhiteSpace(empCode) ? null : Found(await _people.FindAsync(empCode, ct), $"Employee {empCode}");
            await using var db = Db();
            var l = Found(await WithRefs(db.Licenses).FirstOrDefaultAsync(x => x.Id == id, ct), "Licence");
            var asset = assetId == null ? null : Found(await AssetService.WithRefs(db.Assets).FirstOrDefaultAsync(a => a.Id == assetId, ct), "Asset");

            var attrs = ResourceAttrs.For(l);
            if (person != null) attrs.WithAssignee(person);
            if (asset?.Location != null) attrs["location"] = asset.Location.Code;
            await Guard.EnsureAsync(new AccessCheck(Modules.Licenses, Resources.License, Actions.Checkout, attrs), ct);
            Require(l.SeatAssignments.Count < l.Seats, $"All {l.Seats} seats of {l.Name} are in use.");
            Require(l.ExpirationDate == null || l.ExpirationDate >= Today, $"{l.Name} expired on {l.ExpirationDate:dd MMM yyyy}.");
            Require(person == null || !l.SeatAssignments.Any(s => s.AssignedToEmpCode == person.EmpCode), $"{person?.FullName} already has a seat.");

            db.LicenseSeats.Add(new LicenseSeat
            {
                TenantId = Me.TenantId, LicenseId = l.Id, AssignedToEmpCode = person?.EmpCode, AssignedToName = person?.FullName, AssetId = asset?.Id, AssignedAt = Now
            });
            var target = string.Join(" / ", new[] { person == null ? null : $"{person.FullName} ({person.EmpCode})", asset?.AssetTag }.Where(s => s != null));
            Log(db, ItemType.License, l.Id, l.Name, asset?.Location?.Code, HistoryAction.CheckedOut, target, note);
            if (asset != null) Log(db, ItemType.Asset, asset.Id, $"{asset.AssetTag} {asset.Name}", asset.Location?.Code, HistoryAction.CheckedOut, $"Licence {l.Name}", note);
            await db.SaveChangesAsync(ct);
        }

        public async Task CheckinAsync(Guid seatId, string? note, CancellationToken ct = default)
        {
            await using var db = Db();
            var seat = Found(await db.LicenseSeats.Include(s => s.Asset).ThenInclude(a => a!.Location).FirstOrDefaultAsync(s => s.Id == seatId, ct), "Seat");
            var l = Found(await WithRefs(db.Licenses).FirstOrDefaultAsync(x => x.Id == seat.LicenseId, ct), "Licence");
            var attrs = ResourceAttrs.For(l);
            if (seat.Asset?.Location != null) attrs["location"] = seat.Asset.Location.Code;
            if (seat.AssignedToEmpCode != null) attrs["assignedToEmpCode"] = seat.AssignedToEmpCode;
            await Guard.EnsureAsync(new AccessCheck(Modules.Licenses, Resources.License, Actions.Checkin, attrs), ct);
            db.LicenseSeats.Remove(seat);
            Log(db, ItemType.License, l.Id, l.Name, seat.Asset?.Location?.Code, HistoryAction.CheckedIn,
                string.Join(" / ", new[] { seat.AssignedToName, seat.Asset?.AssetTag }.Where(s => s != null)), note);
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Who holds the seats of a licence (people and/or assets).</summary>
        public async Task<List<StockHolder>> SeatsAsync(Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var l = Found(await WithRefs(db.Licenses.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct), "Licence");
            await Guard.EnsureAsync(Check(l, Actions.View), ct);
            var canCheckin = await Guard.CanAsync(Check(l, Actions.Checkin), ct);
            return l.SeatAssignments.OrderBy(s => s.AssignedAt).Select(s => new StockHolder(s.Id,
                string.Join(" · ", new[] { s.AssignedToName == null ? null : $"{s.AssignedToName} ({s.AssignedToEmpCode})", s.Asset == null ? null : $"{s.Asset.AssetTag} {s.Asset.Name}" }.Where(x => x != null)),
                1, s.AssignedAt, canCheckin)).ToList();
        }

        public async Task<string> ExportCsvAsync(string? search, CancellationToken ct = default)
        {
            await Ensure(Modules.Licenses, Resources.License, Actions.Export, ct: ct);
            var rows = await ListAsync(search, ct);
            return Csv.Write(new[] { "Licence", "Manufacturer", "Seats", "Used", "Free", "Purchase date", "Cost", "Expires" },
                rows.Select(r => new object?[] { r.License.Name, r.License.Manufacturer?.Name, r.License.Seats, r.Used, r.Free, r.License.PurchaseDate,
                    r.License.PurchaseCost, r.License.ExpirationDate }));
        }
    }
}
