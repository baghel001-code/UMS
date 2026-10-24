using AMS.Core.Entities;
using AMS.Core.Security;
using Microsoft.EntityFrameworkCore;
using static AMS.Core.Security.Perm;

namespace AMS.Core.Services
{
    public enum LookupKind { Category = 1, Manufacturer = 2, Model = 3, Supplier = 4, Location = 5, Department = 6, Status = 7 }

    /// <summary>Editable shape of any setup record; each kind uses the fields that apply to it.</summary>
    public class LookupItem
    {
        public Guid Id { get; set; }
        public LookupKind Kind { get; set; }
        public string Name { get; set; } = "";
        public string? Code { get; set; }
        public string? Region { get; set; }
        public string? Address { get; set; }
        public CategoryKind CategoryKind { get; set; } = CategoryKind.Asset;
        public int DepreciationMonths { get; set; }
        public StatusType StatusType { get; set; } = StatusType.Deployable;
        public string? Url { get; set; }
        public string? ContactName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? ModelNumber { get; set; }
        public Guid? CategoryId { get; set; }
        public Guid? ManufacturerId { get; set; }
        public string? CategoryName { get; set; }
        public string? ManufacturerName { get; set; }
        public int EolMonths { get; set; }
        /// <summary>How many records use it (cannot delete while in use).</summary>
        public int UsedBy { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }

        public LookupItem Clone() => (LookupItem)MemberwiseClone();
    }

    /// <summary>Drop-down data for forms; a list is empty when UMS does not allow the user to view that setup data.</summary>
    public sealed record Options(List<Category> Categories, List<Manufacturer> Manufacturers, List<AssetModel> Models, List<Supplier> Suppliers,
        List<Location> Locations, List<Department> Departments, List<StatusLabel> Statuses);

    public class LookupService : AmsServiceBase
    {
        public LookupService(IAccessGuard guard, IAmsDbContextFactory factory, TimeProvider time) : base(guard, factory, time) { }

        public static string ResourceOf(LookupKind kind) => kind switch
        {
            LookupKind.Category => Resources.Category,
            LookupKind.Manufacturer => Resources.Manufacturer,
            LookupKind.Model => Resources.Model,
            LookupKind.Supplier => Resources.Supplier,
            LookupKind.Location => Resources.Location,
            LookupKind.Department => Resources.Department,
            _ => Resources.Status,
        };

        private static AccessCheck Check(LookupKind kind, string action, LookupItem? item = null)
        {
            var attrs = new Dictionary<string, string>();
            if (item != null)
            {
                attrs["name"] = item.Name;
                if (kind == LookupKind.Location && !string.IsNullOrEmpty(item.Code)) { attrs["location"] = item.Code; attrs["region"] = item.Region ?? ""; }
            }
            return new AccessCheck(Modules.Setup, ResourceOf(kind), action, attrs);
        }

        public async Task<List<LookupItem>> ListAsync(LookupKind kind, CancellationToken ct = default)
        {
            await Ensure(Modules.Setup, ResourceOf(kind), Actions.View, ct: ct);
            await using var db = Db();
            var items = await LoadAsync(db, kind, ct);
            var visible = await Guard.FilterAsync(items, i => Check(kind, Actions.View, i), ct);
            if (visible.Count == 0) return visible;
            var allowed = await Guard.CanAsync(visible.SelectMany(i => new[] { Check(kind, Actions.Edit, i), Check(kind, Actions.Delete, i) }).ToList(), ct);
            for (var i = 0; i < visible.Count; i++)
            {
                visible[i].CanEdit = allowed[i * 2];
                visible[i].CanDelete = allowed[i * 2 + 1] && visible[i].UsedBy == 0;
            }
            return visible;
        }

        private static async Task<List<LookupItem>> LoadAsync(IAmsDbContext db, LookupKind kind, CancellationToken ct)
        {
            switch (kind)
            {
                case LookupKind.Category:
                {
                    var models = await db.Models.GroupBy(m => m.CategoryId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    var lic = await db.Licenses.Where(l => l.CategoryId != null).GroupBy(l => l.CategoryId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    return (await db.Categories.AsNoTracking().OrderBy(c => c.Kind).ThenBy(c => c.Name).ToListAsync(ct)).Select(c => new LookupItem
                    {
                        Id = c.Id, Kind = kind, Name = c.Name, CategoryKind = c.Kind, DepreciationMonths = c.DepreciationMonths,
                        UsedBy = models.GetValueOrDefault(c.Id) + lic.GetValueOrDefault(c.Id)
                    }).ToList();
                }
                case LookupKind.Manufacturer:
                {
                    var used = await db.Models.GroupBy(m => m.ManufacturerId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    return (await db.Manufacturers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(m => new LookupItem
                    {
                        Id = m.Id, Kind = kind, Name = m.Name, Url = m.Url, Email = m.SupportEmail, Phone = m.SupportPhone, UsedBy = used.GetValueOrDefault(m.Id)
                    }).ToList();
                }
                case LookupKind.Model:
                {
                    var used = await db.Assets.GroupBy(a => a.ModelId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    return (await db.Models.AsNoTracking().Include(m => m.Category).Include(m => m.Manufacturer).OrderBy(m => m.Name).ToListAsync(ct)).Select(m => new LookupItem
                    {
                        Id = m.Id, Kind = kind, Name = m.Name, ModelNumber = m.ModelNumber, CategoryId = m.CategoryId, ManufacturerId = m.ManufacturerId,
                        CategoryName = m.Category?.Name, ManufacturerName = m.Manufacturer?.Name, EolMonths = m.EolMonths, UsedBy = used.GetValueOrDefault(m.Id)
                    }).ToList();
                }
                case LookupKind.Supplier:
                {
                    var used = await db.Assets.Where(a => a.SupplierId != null).GroupBy(a => a.SupplierId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    return (await db.Suppliers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(s => new LookupItem
                    {
                        Id = s.Id, Kind = kind, Name = s.Name, ContactName = s.ContactName, Email = s.Email, Phone = s.Phone, UsedBy = used.GetValueOrDefault(s.Id)
                    }).ToList();
                }
                case LookupKind.Location:
                {
                    var used = await db.Assets.GroupBy(a => a.LocationId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    return (await db.Locations.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct)).Select(l => new LookupItem
                    {
                        Id = l.Id, Kind = kind, Name = l.Name, Code = l.Code, Region = l.Region, Address = l.Address, UsedBy = used.GetValueOrDefault(l.Id)
                    }).ToList();
                }
                case LookupKind.Department:
                {
                    var used = await db.Assets.Where(a => a.DepartmentId != null).GroupBy(a => a.DepartmentId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    return (await db.Departments.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(d => new LookupItem
                    {
                        Id = d.Id, Kind = kind, Name = d.Name, UsedBy = used.GetValueOrDefault(d.Id)
                    }).ToList();
                }
                default:
                {
                    var used = await db.Assets.GroupBy(a => a.StatusLabelId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    return (await db.StatusLabels.AsNoTracking().OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync(ct)).Select(s => new LookupItem
                    {
                        Id = s.Id, Kind = kind, Name = s.Name, StatusType = s.Type, UsedBy = used.GetValueOrDefault(s.Id)
                    }).ToList();
                }
            }
        }

        public async Task SaveAsync(LookupItem x, CancellationToken ct = default)
        {
            Require(!string.IsNullOrWhiteSpace(x.Name), "Name is required.");
            var isNew = x.Id == Guid.Empty;
            await using var db = Db();
            if (!isNew)
            {
                // check against the record as stored, then as edited
                var stored = (await LoadAsync(db, x.Kind, ct)).FirstOrDefault(i => i.Id == x.Id);
                await Guard.EnsureAsync(Check(x.Kind, Actions.Edit, Found(stored, x.Kind.ToString())), ct);
            }
            await Guard.EnsureAsync(Check(x.Kind, isNew ? Actions.Create : Actions.Edit, x), ct);

            var name = x.Name.Trim();
            var id = isNew ? Guid.NewGuid() : x.Id;
            async Task<T> Get<T>(DbSet<T> set) where T : TenantEntity
            {
                if (isNew)
                {
                    var created = Activator.CreateInstance<T>();
                    created.Id = id;
                    created.TenantId = Me.TenantId;
                    set.Add(created);
                    return created;
                }
                return Found(await set.FirstOrDefaultAsync(e => e.Id == id, ct), x.Kind.ToString());
            }

            switch (x.Kind)
            {
                case LookupKind.Category:
                    Require(!await db.Categories.AnyAsync(c => c.Name == name && c.Kind == x.CategoryKind && c.Id != id, ct), $"Category '{name}' already exists.");
                    var cat = await Get(db.Categories);
                    cat.Name = name; cat.Kind = x.CategoryKind; cat.DepreciationMonths = Math.Max(0, x.DepreciationMonths);
                    break;
                case LookupKind.Manufacturer:
                    var man = await Get(db.Manufacturers);
                    man.Name = name; man.Url = x.Url; man.SupportEmail = x.Email; man.SupportPhone = x.Phone;
                    break;
                case LookupKind.Model:
                    Require(x.CategoryId != null && x.ManufacturerId != null, "Choose a category and a manufacturer.");
                    var model = await Get(db.Models);
                    model.Name = name; model.ModelNumber = x.ModelNumber; model.CategoryId = x.CategoryId!.Value; model.ManufacturerId = x.ManufacturerId!.Value;
                    model.EolMonths = Math.Max(0, x.EolMonths);
                    break;
                case LookupKind.Supplier:
                    var sup = await Get(db.Suppliers);
                    sup.Name = name; sup.ContactName = x.ContactName; sup.Email = x.Email; sup.Phone = x.Phone;
                    break;
                case LookupKind.Location:
                    Require(!string.IsNullOrWhiteSpace(x.Code), "Code is required (e.g. S045, DC-NR1, HO-GGN).");
                    var code = x.Code!.Trim().ToUpperInvariant();
                    Require(!await db.Locations.AnyAsync(l => l.Code == code && l.Id != id, ct), $"Location code {code} already exists.");
                    var loc = await Get(db.Locations);
                    loc.Code = code; loc.Name = name; loc.Region = x.Region?.Trim() ?? ""; loc.Address = x.Address;
                    break;
                case LookupKind.Department:
                    var dep = await Get(db.Departments);
                    dep.Name = name;
                    break;
                case LookupKind.Status:
                    var st = await Get(db.StatusLabels);
                    st.Name = name; st.Type = x.StatusType;
                    break;
            }
            Log(db, ItemType.Setup, id, $"{x.Kind}: {name}", x.Kind == LookupKind.Location ? x.Code : null, isNew ? HistoryAction.Created : HistoryAction.Updated);
            await db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(LookupKind kind, Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var item = Found((await LoadAsync(db, kind, ct)).FirstOrDefault(i => i.Id == id), kind.ToString());
            await Guard.EnsureAsync(Check(kind, Actions.Delete, item), ct);
            Require(item.UsedBy == 0, $"{item.Name} is used by {item.UsedBy} record(s).");
            switch (kind)
            {
                case LookupKind.Category: db.Categories.Remove(await db.Categories.FirstAsync(e => e.Id == id, ct)); break;
                case LookupKind.Manufacturer: db.Manufacturers.Remove(await db.Manufacturers.FirstAsync(e => e.Id == id, ct)); break;
                case LookupKind.Model: db.Models.Remove(await db.Models.FirstAsync(e => e.Id == id, ct)); break;
                case LookupKind.Supplier: db.Suppliers.Remove(await db.Suppliers.FirstAsync(e => e.Id == id, ct)); break;
                case LookupKind.Location: db.Locations.Remove(await db.Locations.FirstAsync(e => e.Id == id, ct)); break;
                case LookupKind.Department: db.Departments.Remove(await db.Departments.FirstAsync(e => e.Id == id, ct)); break;
                case LookupKind.Status: db.StatusLabels.Remove(await db.StatusLabels.FirstAsync(e => e.Id == id, ct)); break;
            }
            Log(db, ItemType.Setup, id, $"{kind}: {item.Name}", kind == LookupKind.Location ? item.Code : null, HistoryAction.Deleted);
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Choices for forms and filters. Each list is filtered by the user's SETUP view rights (so a location-scoped manager sees only their locations).</summary>
        public async Task<Options> OptionsAsync(CancellationToken ct = default)
        {
            await using var db = Db();
            var cats = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            var mans = await db.Manufacturers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            var models = await db.Models.AsNoTracking().Include(m => m.Category).Include(m => m.Manufacturer).OrderBy(c => c.Name).ToListAsync(ct);
            var sups = await db.Suppliers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            var locs = await db.Locations.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
            var deps = await db.Departments.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            var stats = await db.StatusLabels.AsNoTracking().OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync(ct);

            var checks = new List<AccessCheck>
            {
                Check(LookupKind.Category, Actions.View), Check(LookupKind.Manufacturer, Actions.View), Check(LookupKind.Model, Actions.View),
                Check(LookupKind.Supplier, Actions.View), Check(LookupKind.Department, Actions.View), Check(LookupKind.Status, Actions.View)
            };
            checks.AddRange(locs.Select(l => Check(LookupKind.Location, Actions.View, new LookupItem { Name = l.Name, Code = l.Code, Region = l.Region })));
            var ok = await Guard.CanAsync(checks, ct);
            return new Options(ok[0] ? cats : new(), ok[1] ? mans : new(), ok[2] ? models : new(), ok[3] ? sups : new(),
                locs.Where((_, i) => ok[6 + i]).ToList(), ok[4] ? deps : new(), ok[5] ? stats : new());
        }
    }
}
