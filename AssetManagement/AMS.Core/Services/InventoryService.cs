using AMS.Core.Entities;
using AMS.Core.Security;
using Microsoft.EntityFrameworkCore;
using static AMS.Core.Security.Perm;

namespace AMS.Core.Services
{
    public enum StockKind { Accessory = 1, Consumable = 2, Component = 3 }

    public class StockInput
    {
        public string Name { get; set; } = "";
        public Guid? CategoryId { get; set; }
        public Guid? ManufacturerId { get; set; }
        public Guid LocationId { get; set; }
        public string? ModelNumber { get; set; }
        public string? Serial { get; set; }
        public int Quantity { get; set; } = 1;
        public int MinQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? Notes { get; set; }

        public static StockInput From(StockItem s) => new()
        {
            Name = s.Name, CategoryId = s.CategoryId, ManufacturerId = s.ManufacturerId, LocationId = s.LocationId, ModelNumber = s.ModelNumber,
            Serial = (s as Component)?.Serial, Quantity = s.Quantity, MinQuantity = s.MinQuantity, UnitCost = s.UnitCost, Notes = s.Notes
        };
    }

    /// <summary>A unit out of stock: accessory with a person, consumable issued to a person, component installed in an asset.</summary>
    public sealed record StockHolder(Guid Id, string Holder, int Quantity, DateTime Since, bool CanCheckin);

    public sealed record StockRow(StockItem Item, int InUse, int Available, bool CanEdit, bool CanDelete, bool CanCheckout, bool CanCheckin)
    {
        public bool Low => Available <= Item.MinQuantity;
    }

    /// <summary>
    /// Quantity-tracked items. Accessories (mouse, headset) are lent to people and returned; consumables (toner, labels) are issued and used up;
    /// components (RAM, SSD) are installed into assets. Each kind is its own UMS module, so access is granted separately.
    /// </summary>
    public class InventoryService : AmsServiceBase
    {
        private readonly IPeopleDirectory _people;

        public InventoryService(IAccessGuard guard, IAmsDbContextFactory factory, IPeopleDirectory people, TimeProvider time) : base(guard, factory, time) =>
            _people = people;

        public static (string Module, string Resource, ItemType Type) Catalogue(StockKind kind) => kind switch
        {
            StockKind.Accessory => (Modules.Accessories, Resources.Accessory, ItemType.Accessory),
            StockKind.Consumable => (Modules.Consumables, Resources.Consumable, ItemType.Consumable),
            _ => (Modules.Components, Resources.Component, ItemType.Component),
        };

        private static AccessCheck Check(StockKind kind, StockItem s, string action, int quantity = 1)
        {
            var (module, resource, _) = Catalogue(kind);
            return new AccessCheck(module, resource, action, ResourceAttrs.For(s, quantity));
        }

        private async Task<List<StockItem>> LoadAllAsync(IAmsDbContext db, StockKind kind, CancellationToken ct) => kind switch
        {
            StockKind.Accessory => (await db.Accessories.AsNoTracking().Include(x => x.Category).Include(x => x.Manufacturer).Include(x => x.Location)
                .Include(x => x.Checkouts).OrderBy(x => x.Name).ToListAsync(ct)).Cast<StockItem>().ToList(),
            StockKind.Consumable => (await db.Consumables.AsNoTracking().Include(x => x.Category).Include(x => x.Manufacturer).Include(x => x.Location)
                .OrderBy(x => x.Name).ToListAsync(ct)).Cast<StockItem>().ToList(),
            _ => (await db.Components.AsNoTracking().Include(x => x.Category).Include(x => x.Manufacturer).Include(x => x.Location)
                .Include(x => x.Installs).OrderBy(x => x.Name).ToListAsync(ct)).Cast<StockItem>().ToList(),
        };

        private static async Task<StockItem> LoadAsync(IAmsDbContext db, StockKind kind, Guid id, CancellationToken ct) => kind switch
        {
            StockKind.Accessory => Found(await db.Accessories.Include(x => x.Category).Include(x => x.Location).Include(x => x.Checkouts)
                .FirstOrDefaultAsync(x => x.Id == id, ct), "Accessory"),
            StockKind.Consumable => Found(await db.Consumables.Include(x => x.Category).Include(x => x.Location)
                .FirstOrDefaultAsync(x => x.Id == id, ct), "Consumable"),
            _ => Found(await db.Components.Include(x => x.Category).Include(x => x.Location).Include(x => x.Installs)
                .FirstOrDefaultAsync(x => x.Id == id, ct), "Component"),
        };

        /// <summary>Units currently out (consumables are used up, so they are never "in use").</summary>
        public static int InUse(StockItem s) => s switch
        {
            Accessory a => a.Checkouts.Count,
            Component c => c.Installs.Sum(i => i.Quantity),
            _ => 0
        };

        public async Task<List<StockRow>> ListAsync(StockKind kind, string? search, CancellationToken ct = default)
        {
            await using var db = Db();
            var all = (await LoadAllAsync(db, kind, ct))
                .Where(s => string.IsNullOrWhiteSpace(search) || $"{s.Name} {s.ModelNumber} {s.Category?.Name} {s.Location?.Code}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            var visible = await Guard.FilterAsync(all, s => Check(kind, s, Actions.View), ct);
            if (visible.Count == 0) return new List<StockRow>();
            string[] actions = { Actions.Edit, Actions.Delete, Actions.Checkout, Actions.Checkin };
            var allowed = await Guard.CanAsync(visible.SelectMany(s => actions.Select(a => Check(kind, s, a))).ToList(), ct);
            return visible.Select((s, i) =>
            {
                var o = i * actions.Length;
                var used = InUse(s);
                var available = s.Quantity - used;
                return new StockRow(s, used, available, allowed[o], allowed[o + 1], allowed[o + 2] && available > 0, allowed[o + 3] && kind != StockKind.Consumable && used > 0);
            }).ToList();
        }

        public async Task<List<StockHolder>> HoldersAsync(StockKind kind, Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var s = await LoadAsync(db, kind, id, ct);
            await Guard.EnsureAsync(Check(kind, s, Actions.View), ct);
            var canCheckin = kind != StockKind.Consumable && await Guard.CanAsync(Check(kind, s, Actions.Checkin), ct);
            return kind switch
            {
                StockKind.Accessory => ((Accessory)s).Checkouts.OrderBy(c => c.AssignedAt)
                    .Select(c => new StockHolder(c.Id, $"{c.AssignedToName} ({c.AssignedToEmpCode})", 1, c.AssignedAt, canCheckin)).ToList(),
                StockKind.Consumable => (await db.ConsumableIssues.AsNoTracking().Where(c => c.ConsumableId == id).OrderByDescending(c => c.IssuedAt).ToListAsync(ct))
                    .Select(c => new StockHolder(c.Id, $"{c.IssuedToName} ({c.IssuedToEmpCode})", c.Quantity, c.IssuedAt, false)).ToList(),
                _ => (await db.ComponentInstalls.AsNoTracking().Include(c => c.Asset).Where(c => c.ComponentId == id).OrderBy(c => c.InstalledAt).ToListAsync(ct))
                    .Select(c => new StockHolder(c.Id, $"{c.Asset?.AssetTag} {c.Asset?.Name}", c.Quantity, c.InstalledAt, canCheckin)).ToList(),
            };
        }

        private static async Task ApplyAsync(IAmsDbContext db, StockItem s, StockInput x, int inUse, CancellationToken ct)
        {
            Require(!string.IsNullOrWhiteSpace(x.Name), "Name is required.");
            Require(x.Quantity >= 0 && x.MinQuantity >= 0, "Quantities cannot be negative.");
            Require(x.Quantity >= inUse, $"{inUse} units are out; quantity cannot be less than that.");
            Require(x.UnitCost >= 0, "Cost cannot be negative.");
            s.Name = x.Name.Trim();
            s.CategoryId = x.CategoryId;
            s.Category = x.CategoryId == null ? null : await db.Categories.FirstOrDefaultAsync(c => c.Id == x.CategoryId, ct);
            s.ManufacturerId = x.ManufacturerId;
            s.Location = Found(await db.Locations.FirstOrDefaultAsync(l => l.Id == x.LocationId, ct), "Location");
            s.LocationId = s.Location.Id;
            s.ModelNumber = string.IsNullOrWhiteSpace(x.ModelNumber) ? null : x.ModelNumber.Trim();
            s.Quantity = x.Quantity;
            s.MinQuantity = x.MinQuantity;
            s.UnitCost = x.UnitCost;
            s.Notes = x.Notes;
            if (s is Component c) c.Serial = string.IsNullOrWhiteSpace(x.Serial) ? null : x.Serial.Trim();
        }

        public async Task CreateAsync(StockKind kind, StockInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            StockItem s = kind switch { StockKind.Accessory => new Accessory(), StockKind.Consumable => new Consumable(), _ => new Component() };
            s.TenantId = Me.TenantId;
            await ApplyAsync(db, s, input, 0, ct);
            await Guard.EnsureAsync(Check(kind, s, Actions.Create, Math.Max(1, s.Quantity)), ct);
            switch (s)
            {
                case Accessory a: db.Accessories.Add(a); break;
                case Consumable c: db.Consumables.Add(c); break;
                case Component c: db.Components.Add(c); break;
            }
            Log(db, Catalogue(kind).Type, s.Id, s.Name, s.Location?.Code, HistoryAction.Created, $"Qty {s.Quantity}");
            await db.SaveChangesAsync(ct);
        }

        public async Task UpdateAsync(StockKind kind, Guid id, StockInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            var s = await LoadAsync(db, kind, id, ct);
            await Guard.EnsureAsync(Check(kind, s, Actions.Edit), ct);
            await ApplyAsync(db, s, input, InUse(s), ct);
            await Guard.EnsureAsync(Check(kind, s, Actions.Edit), ct);
            Log(db, Catalogue(kind).Type, s.Id, s.Name, s.Location?.Code, HistoryAction.Updated, $"Qty {s.Quantity}");
            await db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(StockKind kind, Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var s = await LoadAsync(db, kind, id, ct);
            await Guard.EnsureAsync(Check(kind, s, Actions.Delete, Math.Max(1, s.Quantity)), ct);
            Require(InUse(s) == 0, "Check in all units before deleting.");
            switch (s)
            {
                case Accessory a: db.Accessories.Remove(a); break;
                case Consumable c:
                    db.ConsumableIssues.RemoveRange(await db.ConsumableIssues.Where(i => i.ConsumableId == c.Id).ToListAsync(ct));
                    db.Consumables.Remove(c);
                    break;
                case Component c: db.Components.Remove(c); break;
            }
            Log(db, Catalogue(kind).Type, s.Id, s.Name, s.Location?.Code, HistoryAction.Deleted);
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Accessory → lend one to a person · Consumable → issue a quantity to a person · Component → install a quantity into an asset.</summary>
        public async Task CheckoutAsync(StockKind kind, Guid id, string? empCode, Guid? assetId, int quantity, string? note, CancellationToken ct = default)
        {
            quantity = kind == StockKind.Accessory ? 1 : quantity;
            Require(quantity >= 1, "Quantity must be at least 1.");
            Person? person = null;
            if (kind == StockKind.Component) Require(assetId != null, "Choose the asset the component goes into.");
            else
            {
                Require(!string.IsNullOrWhiteSpace(empCode), "Choose a person.");
                person = Found(await _people.FindAsync(empCode!, ct), $"Employee {empCode}");
            }

            await using var db = Db();
            var s = await LoadAsync(db, kind, id, ct);
            var asset = assetId == null ? null : Found(await AssetService.WithRefs(db.Assets).FirstOrDefaultAsync(a => a.Id == assetId, ct), "Asset");
            var (module, resource, type) = Catalogue(kind);
            var attrs = ResourceAttrs.For(s, quantity);
            if (person != null) attrs.WithAssignee(person);
            if (asset != null) { attrs["assetTag"] = asset.AssetTag; attrs["assetLocation"] = asset.Location?.Code ?? ""; }
            await Guard.EnsureAsync(new AccessCheck(module, resource, Actions.Checkout, attrs), ct);

            var available = s.Quantity - InUse(s);
            Require(quantity <= available, $"Only {available} of {s.Name} available.");
            switch (s)
            {
                case Accessory a:
                    Require(!a.Checkouts.Any(c => c.AssignedToEmpCode == person!.EmpCode), $"{person!.FullName} already has one.");
                    db.AccessoryCheckouts.Add(new AccessoryCheckout { TenantId = Me.TenantId, AccessoryId = a.Id, AssignedToEmpCode = person.EmpCode, AssignedToName = person.FullName, AssignedAt = Now });
                    Log(db, type, s.Id, s.Name, s.Location?.Code, HistoryAction.CheckedOut, $"{person.FullName} ({person.EmpCode})", note);
                    break;
                case Consumable c:
                    c.Quantity -= quantity;
                    db.ConsumableIssues.Add(new ConsumableIssue { TenantId = Me.TenantId, ConsumableId = c.Id, IssuedToEmpCode = person!.EmpCode, IssuedToName = person.FullName, Quantity = quantity, IssuedAt = Now });
                    Log(db, type, s.Id, s.Name, s.Location?.Code, HistoryAction.CheckedOut, $"{quantity} × to {person.FullName} ({person.EmpCode})", note);
                    break;
                case Component c:
                    db.ComponentInstalls.Add(new ComponentInstall { TenantId = Me.TenantId, ComponentId = c.Id, AssetId = asset!.Id, Quantity = quantity, InstalledAt = Now });
                    Log(db, type, s.Id, s.Name, s.Location?.Code, HistoryAction.CheckedOut, $"{quantity} × into {asset.AssetTag}", note);
                    Log(db, ItemType.Asset, asset.Id, $"{asset.AssetTag} {asset.Name}", asset.Location?.Code, HistoryAction.Updated, $"Installed {quantity} × {c.Name}", note);
                    break;
            }
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Returns a lent accessory or removes an installed component (back into stock).</summary>
        public async Task CheckinAsync(StockKind kind, Guid itemId, Guid holderId, string? note, CancellationToken ct = default)
        {
            Require(kind != StockKind.Consumable, "Consumables are used up and cannot be checked in.");
            await using var db = Db();
            var s = await LoadAsync(db, kind, itemId, ct);
            await Guard.EnsureAsync(Check(kind, s, Actions.Checkin), ct);
            var type = Catalogue(kind).Type;
            if (s is Accessory a)
            {
                var c = Found(a.Checkouts.FirstOrDefault(x => x.Id == holderId), "Checkout");
                db.AccessoryCheckouts.Remove(c);
                Log(db, type, s.Id, s.Name, s.Location?.Code, HistoryAction.CheckedIn, $"{c.AssignedToName} ({c.AssignedToEmpCode})", note);
            }
            else if (s is Component comp)
            {
                var i = Found(comp.Installs.FirstOrDefault(x => x.Id == holderId), "Installation");
                var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == i.AssetId, ct);
                db.ComponentInstalls.Remove(i);
                Log(db, type, s.Id, s.Name, s.Location?.Code, HistoryAction.CheckedIn, $"{i.Quantity} × from {asset?.AssetTag}", note);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
