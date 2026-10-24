using AMS.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Infrastructure.Persistence
{
    /// <summary>
    /// Demo data for a tenant (development only). People and location codes match the UMS demo directory
    /// (e.g. Priya Verma VMM10002 at store S045, head office HO-GGN) so UMS scopes and policies give realistic results.
    /// </summary>
    public static class AmsDemoSeeder
    {
        private static readonly (string Code, string Name)[] People =
        {
            ("VMM10001", "Aarav Sharma"), ("VMM10002", "Priya Verma"), ("VMM10003", "Rohit Gupta"), ("VMM10004", "Neha Singh"),
            ("VMM10005", "Vikram Mehta"), ("VMM10006", "Kavya Nair"), ("VMM10007", "Imran Khan"), ("VMM10008", "Sneha Iyer"),
            ("VMM10009", "Ankit Saxena"), ("VMM10010", "Rahul Agarwal"),
        };

        public static async Task<bool> SeedIfEmptyAsync(AmsDbContext db, DateTime now, CancellationToken ct = default)
        {
            if (await db.Locations.AnyAsync(ct)) return false;
            var t = db.TenantId;
            var rnd = new Random(20261024);
            var today = DateOnly.FromDateTime(now);
            T Add<T>(T e) where T : TenantEntity { e.TenantId = t; db.Add(e); return e; }

            // ---- setup ----
            var locs = new[]
            {
                ("HO-GGN", "Head Office, Gurugram", "North"), ("S045", "Store 045 – Rohini, Delhi", "North"), ("S046", "Store 046 – Pitampura, Delhi", "North"),
                ("S012", "Store 012 – Noida Sec 18", "North"), ("DC-NR1", "DC North – Bilaspur", "North"), ("S210", "Store 210 – Andheri, Mumbai", "West"),
                ("S330", "Store 330 – Koramangala, Bengaluru", "South"),
            }.Select(l => Add(new Location { Code = l.Item1, Name = l.Item2, Region = l.Item3 })).ToDictionary(l => l.Code);
            var depts = new[] { "IT", "Finance", "HR", "Store Operations", "Supply Chain", "Marketing" }.Select(n => Add(new Department { Name = n })).ToDictionary(d => d.Name);

            var ready = Add(new StatusLabel { Name = "Ready to Deploy", Type = StatusType.Deployable });
            var deployed = Add(new StatusLabel { Name = "In Use", Type = StatusType.Deployable });
            var repair = Add(new StatusLabel { Name = "Out for Repair", Type = StatusType.Pending });
            var broken = Add(new StatusLabel { Name = "Broken", Type = StatusType.Undeployable });
            Add(new StatusLabel { Name = "Lost / Stolen", Type = StatusType.Undeployable });
            var retired = Add(new StatusLabel { Name = "Retired", Type = StatusType.Archived });

            Category Cat(string n, CategoryKind k, int dep) => Add(new Category { Name = n, Kind = k, DepreciationMonths = dep });
            var laptop = Cat("Laptops", CategoryKind.Asset, 36);
            var desktop = Cat("Desktops", CategoryKind.Asset, 48);
            var pos = Cat("POS Terminals", CategoryKind.Asset, 60);
            var printer = Cat("Printers", CategoryKind.Asset, 60);
            var mobile = Cat("Mobile Devices", CategoryKind.Asset, 24);
            var network = Cat("Network Equipment", CategoryKind.Asset, 60);
            var server = Cat("Servers", CategoryKind.Asset, 60);
            var furniture = Cat("Furniture & Fixtures", CategoryKind.Asset, 120);
            var swOffice = Cat("Productivity Software", CategoryKind.License, 0);
            var swSecurity = Cat("Security Software", CategoryKind.License, 0);
            var accCat = Cat("Peripherals", CategoryKind.Accessory, 0);
            var conCat = Cat("Printer Supplies", CategoryKind.Consumable, 0);
            var compCat = Cat("Memory & Storage", CategoryKind.Component, 0);

            var mans = new[] { "Dell", "HP", "Lenovo", "Apple", "Samsung", "Zebra", "Cisco", "Microsoft", "Godrej", "Epson" }
                .Select(n => Add(new Manufacturer { Name = n, Url = $"https://www.{n.ToLowerInvariant()}.com" })).ToDictionary(m => m.Name);
            var sups = new[] { ("Redington India", "Sanjay Kapoor"), ("Ingram Micro India", "Meera Pillai"), ("Croma Business", "Arjun Rao"), ("Godrej Interio", "Farah Ali") }
                .Select(s => Add(new Supplier { Name = s.Item1, ContactName = s.Item2, Email = $"sales@{s.Item1.Split(' ')[0].ToLowerInvariant()}.example", Phone = "+91 11 4000 " + rnd.Next(1000, 9999) }))
                .ToList();

            AssetModel Model(string n, string no, Category c, string m, int eol) => Add(new AssetModel { Name = n, ModelNumber = no, Category = c, Manufacturer = mans[m], EolMonths = eol });
            var models = new (AssetModel Model, decimal Cost, string Prefix, int Warranty)[]
            {
                (Model("Latitude 5440", "LAT-5440", laptop, "Dell", 48), 78000m, "LT", 36),
                (Model("ThinkPad T14 Gen 4", "21HD", laptop, "Lenovo", 48), 92000m, "LT", 36),
                (Model("MacBook Pro 14 M3", "MRX33", laptop, "Apple", 60), 189000m, "LT", 12),
                (Model("OptiPlex 7010", "OPT-7010", desktop, "Dell", 60), 56000m, "DT", 36),
                (Model("ProDesk 400 G9", "6B2", desktop, "HP", 60), 52000m, "DT", 36),
                (Model("TC52 Handheld", "TC52", mobile, "Zebra", 48), 64000m, "HH", 24),
                (Model("Galaxy Tab Active4 Pro", "SM-T636", mobile, "Samsung", 36), 58000m, "TB", 12),
                (Model("POS All-in-One TP-15", "TP15", pos, "HP", 72), 115000m, "PO", 36),
                (Model("LaserJet Pro M404dn", "W1A53A", printer, "HP", 60), 28000m, "PR", 12),
                (Model("ZD421 Label Printer", "ZD4A042", printer, "Zebra", 60), 32000m, "PR", 24),
                (Model("Catalyst 9200 48-port", "C9200-48P", network, "Cisco", 84), 245000m, "NW", 60),
                (Model("PowerEdge R760", "R760", server, "Dell", 84), 640000m, "SV", 36),
                (Model("Ergonomic Chair", "GC-ERGO", furniture, "Godrej", 120), 18000m, "FN", 12),
            };

            // ---- assets ----
            var plan = new (string Loc, int Count)[] { ("HO-GGN", 26), ("S045", 9), ("S046", 6), ("S012", 5), ("DC-NR1", 7), ("S210", 4), ("S330", 4) };
            var holders = new Dictionary<string, string[]>
            {
                ["HO-GGN"] = new[] { "VMM10004", "VMM10005", "VMM10006", "VMM10008", "VMM10009", "VMM10010" },
                ["S045"] = new[] { "VMM10001", "VMM10002", "VMM10003" },
                ["S046"] = new[] { "VMM10001" },
                ["DC-NR1"] = new[] { "VMM10007" },
            };
            var assets = new List<Asset>();
            var n = 1000;
            foreach (var (loc, count) in plan)
            {
                for (var i = 0; i < count; i++)
                {
                    var pick = loc == "HO-GGN" ? models[rnd.Next(0, 5) switch { 4 => rnd.Next(10, 13), var k => k }]
                        : loc.StartsWith("DC") ? models[new[] { 5, 9, 3, 10 }[rnd.Next(4)]]
                        : models[new[] { 7, 7, 5, 6, 8, 9, 3 }[rnd.Next(7)]];
                    var bought = today.AddDays(-rnd.Next(60, 1300));
                    var a = Add(new Asset
                    {
                        AssetTag = $"VMM-{pick.Prefix}-{++n}", Name = pick.Model.Name, Serial = $"{pick.Model.ModelNumber}-{rnd.Next(100000, 999999)}",
                        Model = pick.Model, StatusLabel = ready, Location = locs[loc], Department = depts[loc == "HO-GGN" ? new[] { "IT", "Finance", "HR", "Marketing" }[rnd.Next(4)] : loc.StartsWith("DC") ? "Supply Chain" : "Store Operations"],
                        Supplier = sups[rnd.Next(sups.Count)], PurchaseDate = bought, PurchaseCost = Math.Round(pick.Cost * (decimal)(0.9 + rnd.NextDouble() * 0.2) / 100m) * 100m,
                        OrderNumber = $"PO-{bought:yyMM}-{rnd.Next(100, 999)}", WarrantyMonths = pick.Warranty,
                        Requestable = pick.Model.Category == laptop || pick.Model.Category == mobile,
                        LastAuditDate = today.AddDays(-rnd.Next(30, 400)), CreatedAt = now.AddDays(-rnd.Next(30, 900)), UpdatedAt = now.AddDays(-rnd.Next(1, 30)),
                    });
                    a.NextAuditDate = a.LastAuditDate!.Value.AddMonths(12);
                    assets.Add(a);
                }
            }

            string NameOf(string code) => People.First(p => p.Code == code).Name;
            void Issue(Asset a, string code, int daysAgo, int? dueInDays = null)
            {
                a.AssignedToEmpCode = code;
                a.AssignedToName = NameOf(code);
                a.AssignedAt = now.AddDays(-daysAgo);
                a.ExpectedCheckin = dueInDays == null ? null : today.AddDays(dueInDays.Value);
                a.StatusLabel = deployed;
                History(a, HistoryAction.CheckedOut, now.AddDays(-daysAgo), "VMM10010", $"{a.AssignedToName} ({code})");
            }
            void History(Asset a, HistoryAction action, DateTime at, string by, string? target = null, string? note = null) =>
                Add(new HistoryEntry { At = at, ActorEmpCode = by, ActorName = NameOf(by), ItemType = ItemType.Asset, ItemId = a.Id, ItemName = $"{a.AssetTag} {a.Name}", LocationCode = a.Location!.Code, Action = action, Target = target, Note = note });

            foreach (var a in assets) History(a, HistoryAction.Created, a.CreatedAt, "VMM10010");

            // people get their kit; a handful of free laptops stay requestable
            foreach (var (loc, codes) in holders)
            {
                var pool = assets.Where(a => a.Location!.Code == loc && a.Model!.Category != network && a.Model.Category != server && a.Model.Category != pos).ToList();
                for (var i = 0; i < codes.Length && i < pool.Count; i++) Issue(pool[i], codes[i], rnd.Next(20, 400));
            }
            var overdue = assets.FirstOrDefault(a => a.Location!.Code == "HO-GGN" && !a.IsCheckedOut && a.Model!.Category == laptop);
            if (overdue != null) Issue(overdue, "VMM10006", 40, -5);          // loaner not returned on time
            var under = assets.Where(a => !a.IsCheckedOut && a.Model!.Category != laptop).Take(3).ToList();
            if (under.Count > 0) under[0].StatusLabel = repair;
            if (under.Count > 1) under[1].StatusLabel = broken;
            if (under.Count > 2) under[2].StatusLabel = retired;
            foreach (var a in assets.Take(6)) a.NextAuditDate = today.AddDays(rnd.Next(-20, 25));   // audits due soon / overdue
            foreach (var a in assets.Skip(10).Take(4)) { a.PurchaseDate = today.AddMonths(-a.WarrantyMonths).AddDays(rnd.Next(10, 80)); }  // warranty ending

            // ---- maintenance ----
            if (under.Count > 0)
                Add(new Maintenance { Asset = under[0], Type = MaintenanceType.Repair, Title = "Display flicker – panel replacement", Supplier = sups[0], StartDate = today.AddDays(-6), Cost = 8500, IsWarranty = true });
            foreach (var a in assets.Where(a => a.Model!.Category == pos).Take(3))
                Add(new Maintenance { Asset = a, Type = MaintenanceType.Maintenance, Title = "Quarterly POS cleaning & firmware update", Supplier = sups[1], StartDate = today.AddDays(-40), CompletionDate = today.AddDays(-39), Cost = 1200 });
            var srv = assets.FirstOrDefault(a => a.Model!.Category == server);
            if (srv != null) Add(new Maintenance { Asset = srv, Type = MaintenanceType.Upgrade, Title = "RAM upgrade to 512 GB", Supplier = sups[0], StartDate = today.AddDays(-2), Cost = 145000 });

            // ---- licences ----
            License Lic(string name, int seats, string man, Category c, decimal cost, int expiresInDays) => Add(new License
            {
                Name = name, Seats = seats, Manufacturer = mans[man], Category = c, Supplier = sups[1], PurchaseCost = cost, PurchaseDate = today.AddDays(-300),
                ExpirationDate = today.AddDays(expiresInDays), ProductKey = $"{rnd.Next(10000, 99999)}-{rnd.Next(10000, 99999)}-{rnd.Next(10000, 99999)}-{rnd.Next(10000, 99999)}"
            });
            var m365 = Lic("Microsoft 365 Business Premium", 25, "Microsoft", swOffice, 540000, 65);
            var win = Lic("Windows 11 Pro (volume)", 40, "Microsoft", swOffice, 620000, 3000);
            var av = Lic("Endpoint Protection Suite", 60, "Cisco", swSecurity, 360000, 200);
            Lic("Adobe Acrobat Pro", 5, "HP", swOffice, 120000, 20);
            foreach (var p in People.Take(9))
                Add(new LicenseSeat { License = m365, AssignedToEmpCode = p.Code, AssignedToName = p.Name, AssignedAt = now.AddDays(-rnd.Next(10, 200)) });
            foreach (var a in assets.Where(a => a.Model!.Category == laptop || a.Model.Category == desktop).Take(18))
            {
                Add(new LicenseSeat { License = win, Asset = a, AssignedAt = now.AddDays(-rnd.Next(10, 200)) });
                Add(new LicenseSeat { License = av, Asset = a, AssignedAt = now.AddDays(-rnd.Next(10, 200)) });
            }

            // ---- accessories, consumables, components ----
            Accessory Acc(string name, string man, string loc, int qty, int min, decimal cost) =>
                Add(new Accessory { Name = name, Manufacturer = mans[man], Category = accCat, Location = locs[loc], Quantity = qty, MinQuantity = min, UnitCost = cost });
            var mouse = Acc("Wireless Mouse", "Dell", "HO-GGN", 40, 8, 900);
            var headset = Acc("USB Headset", "HP", "HO-GGN", 15, 4, 2400);
            var scanner = Acc("Barcode Scanner (USB)", "Zebra", "S045", 6, 2, 7800);
            Acc("Docking Station WD19", "Dell", "HO-GGN", 6, 3, 14500);
            foreach (var code in new[] { "VMM10004", "VMM10005", "VMM10008", "VMM10010" })
                Add(new AccessoryCheckout { Accessory = mouse, AssignedToEmpCode = code, AssignedToName = NameOf(code), AssignedAt = now.AddDays(-rnd.Next(5, 120)) });
            Add(new AccessoryCheckout { Accessory = headset, AssignedToEmpCode = "VMM10006", AssignedToName = NameOf("VMM10006"), AssignedAt = now.AddDays(-30) });
            foreach (var code in new[] { "VMM10002", "VMM10003", "VMM10001", "VMM10002" }.Distinct())
                Add(new AccessoryCheckout { Accessory = scanner, AssignedToEmpCode = code, AssignedToName = NameOf(code), AssignedAt = now.AddDays(-rnd.Next(5, 120)) });

            Consumable Con(string name, string man, string loc, int qty, int min, decimal cost) =>
                Add(new Consumable { Name = name, Manufacturer = mans[man], Category = conCat, Location = locs[loc], Quantity = qty, MinQuantity = min, UnitCost = cost });
            var toner = Con("HP 76A Toner Cartridge", "HP", "HO-GGN", 9, 4, 6800);
            var rolls = Con("Thermal Receipt Roll 79mm (box of 50)", "Epson", "S045", 3, 5, 1450);
            Con("Shelf Label Roll (Zebra)", "Zebra", "DC-NR1", 24, 6, 650);
            Add(new ConsumableIssue { Consumable = toner, IssuedToEmpCode = "VMM10006", IssuedToName = NameOf("VMM10006"), Quantity = 1, IssuedAt = now.AddDays(-12) });
            Add(new ConsumableIssue { Consumable = rolls, IssuedToEmpCode = "VMM10001", IssuedToName = NameOf("VMM10001"), Quantity = 4, IssuedAt = now.AddDays(-3) });

            Component Comp(string name, string man, string loc, int qty, int min, decimal cost) =>
                Add(new Component { Name = name, Manufacturer = mans[man], Category = compCat, Location = locs[loc], Quantity = qty, MinQuantity = min, UnitCost = cost });
            var ram = Comp("16 GB DDR5 SO-DIMM", "Samsung", "HO-GGN", 20, 4, 5200);
            Comp("1 TB NVMe SSD", "Samsung", "HO-GGN", 8, 2, 7400);
            foreach (var a in assets.Where(a => a.Model!.Category == laptop).Take(3))
                Add(new ComponentInstall { Component = ram, Asset = a, Quantity = 1, InstalledAt = now.AddDays(-rnd.Next(10, 100)) });

            // ---- requests ----
            var free = assets.Where(a => a.Requestable && !a.IsCheckedOut && a.StatusLabel == ready).ToList();
            void Req(Asset a, string code, string reason, int daysAgo) =>
                Add(new AssetRequest { Asset = a, RequestedByEmpCode = code, RequestedByName = NameOf(code), Reason = reason, RequestedAt = now.AddDays(-daysAgo) });
            if (free.Count > 0) Req(free[0], "VMM10002", "Need a laptop for month-end stock reconciliation at S045.", 2);
            if (free.Count > 1) Req(free[1], "VMM10010", "Spare laptop for vendor demo days.", 1);          // the approver's own request
            if (free.Count > 2) Req(free[2], "VMM10004", "Current laptop battery fails within an hour.", 4);

            await db.SaveChangesAsync(ct);
            return true;
        }
    }
}
