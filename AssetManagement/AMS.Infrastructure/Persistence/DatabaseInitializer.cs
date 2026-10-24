using AMS.Application.Interfaces;
using AMS.Domain.Enums;
using AMS.Domain.Models;
using AMS.Domain.Requests;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AMS.Infrastructure.Persistence
{
    /// <summary>
    /// Development convenience (Ams:Database:AutoMigrate): creates the database if missing, the tables once, and (re)creates the views and
    /// procedures on every start. Production databases are set up by running database/*.sql.
    /// </summary>
    public sealed class DatabaseInitializer
    {
        private readonly AmsDatabaseOptions _options;
        private readonly DapperHelper _db;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(AmsDatabaseOptions options, DapperHelper db, ILogger<DatabaseInitializer> logger)
        {
            _options = options;
            _db = db;
            _logger = logger;
        }

        public static IEnumerable<string> Batches(string script) =>
            Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(b => !string.IsNullOrWhiteSpace(b));

        public async Task InitialiseAsync(string scriptFolder)
        {
            var builder = new SqlConnectionStringBuilder(_options.ConnectionString);
            var database = builder.InitialCatalog;
            builder.InitialCatalog = "master";
            await using (var master = new SqlConnection(builder.ConnectionString))
            {
                if (await master.ExecuteScalarAsync<int?>("SELECT database_id FROM sys.databases WHERE name = @database", new { database }) == null)
                {
                    _logger.LogInformation("Creating database {Database}", database);
                    await master.ExecuteAsync($"CREATE DATABASE [{database.Replace("]", "]]")}]");
                }
            }

            await using (var conn = _db.CreateConnection())
            {
                var hasTables = await conn.ExecuteScalarAsync<int?>("SELECT OBJECT_ID('dbo.Assets')") != null;
                foreach (var file in Directory.GetFiles(scriptFolder, "*.sql").OrderBy(f => f))
                {
                    var name = Path.GetFileName(file);
                    if (hasTables && name.Contains("schema", StringComparison.OrdinalIgnoreCase)) continue;   // tables exist: only refresh views/procedures
                    _logger.LogInformation("Running {Script}", name);
                    foreach (var batch in Batches(await File.ReadAllTextAsync(file)))
                        await _db.ExecuteBatchAsync(batch);
                }
            }
        }
    }

    /// <summary>
    /// Development demo data for one UMS tenant, written through the same stored procedures the API uses. People and location codes match
    /// the UMS demo directory (e.g. Priya Verma VMM10002 at store S045, head office HO-GGN) so UMS scopes and policies behave realistically.
    /// </summary>
    public sealed class DemoDataSeeder
    {
        private static readonly (string Code, string Name, string Email)[] People =
        {
            ("VMM10001", "Aarav Sharma", "aarav.sharma@vmm.example"), ("VMM10002", "Priya Verma", "priya.verma@vmm.example"),
            ("VMM10003", "Rohit Gupta", "rohit.gupta@vmm.example"), ("VMM10004", "Neha Singh", "neha.singh@vmm.example"),
            ("VMM10005", "Vikram Mehta", "vikram.mehta@vmm.example"), ("VMM10006", "Kavya Nair", "kavya.nair@vmm.example"),
            ("VMM10007", "Imran Khan", "imran.khan@vmm.example"), ("VMM10008", "Sneha Iyer", "sneha.iyer@vmm.example"),
            ("VMM10009", "Ankit Saxena", "ankit.saxena@vmm.example"), ("VMM10010", "Rahul Agarwal", "rahul.agarwal@vmm.example"),
        };

        private readonly DapperHelper _db;
        private readonly SetupRepository _setup;
        private readonly AssetRepository _assets;
        private readonly LicenseRepository _licenses;
        private readonly StockRepository _stock;
        private readonly MaintenanceRepository _maintenance;
        private readonly RequestRepository _requests;
        private readonly IBusinessClock _clock;
        private readonly ILogger<DemoDataSeeder> _logger;

        public DemoDataSeeder(DapperHelper db, IBusinessClock clock, ILogger<DemoDataSeeder> logger)
        {
            _db = db;
            _setup = new SetupRepository(db);
            _assets = new AssetRepository(db);
            _licenses = new LicenseRepository(db);
            _stock = new StockRepository(db);
            _maintenance = new MaintenanceRepository(db);
            _requests = new RequestRepository(db);
            _clock = clock;
            _logger = logger;
        }

        private static Person P((string Code, string Name, string Email) p) => new(p.Code, p.Name, p.Email, "", "", "", "", "Permanent", "Active");
        private static (string Code, string Name, string Email) Who(string code) => People.First(p => p.Code == code);

        public async Task<bool> SeedIfEmptyAsync(Guid tenant)
        {
            if ((await _setup.ListAsync(tenant, SetupKind.Location)).Count > 0) return false;
            _logger.LogInformation("Seeding AMS demo data for tenant {Tenant}", tenant);
            var now = _clock.Now;
            var today = now.Date;
            var rnd = new Random(20261024);
            var w = new WriteContext(tenant, now.AddDays(-400), "VMM10010", "Rahul Agarwal");
            async Task<int> Setup(SetupItem item) => (await _setup.SaveAsync(w, item)).RecordId;

            // ---- setup
            var locs = new Dictionary<string, int>();
            foreach (var (code, name, region) in new[]
            {
                ("HO-GGN", "Head Office, Gurugram", "North"), ("S045", "Store 045 – Rohini, Delhi", "North"), ("S046", "Store 046 – Pitampura, Delhi", "North"),
                ("S012", "Store 012 – Noida Sec 18", "North"), ("DC-NR1", "DC North – Bilaspur", "North"), ("S210", "Store 210 – Andheri, Mumbai", "West"),
                ("S330", "Store 330 – Koramangala, Bengaluru", "South"),
            })
                locs[code] = await Setup(new SetupItem { Kind = SetupKind.Location, Code = code, Name = name, Region = region });
            var depts = new Dictionary<string, int>();
            foreach (var n in new[] { "IT", "Finance", "HR", "Store Operations", "Supply Chain", "Marketing" })
                depts[n] = await Setup(new SetupItem { Kind = SetupKind.Department, Name = n });
            async Task<int> Status(string n, StatusType t) => await Setup(new SetupItem { Kind = SetupKind.Status, Name = n, StatusType = t });
            var ready = await Status("Ready to Deploy", StatusType.Deployable);
            var repair = await Status("Out for Repair", StatusType.Pending);
            var broken = await Status("Broken", StatusType.Undeployable);
            await Status("Lost / Stolen", StatusType.Undeployable);
            var retired = await Status("Retired", StatusType.Archived);

            async Task<int> Cat(string n, CategoryKind k, int dep) => await Setup(new SetupItem { Kind = SetupKind.Category, Name = n, CategoryKind = k, DepreciationMonths = dep });
            var laptop = await Cat("Laptops", CategoryKind.Asset, 36);
            var desktop = await Cat("Desktops", CategoryKind.Asset, 48);
            var pos = await Cat("POS Terminals", CategoryKind.Asset, 60);
            var printer = await Cat("Printers", CategoryKind.Asset, 60);
            var mobile = await Cat("Mobile Devices", CategoryKind.Asset, 24);
            var network = await Cat("Network Equipment", CategoryKind.Asset, 60);
            var server = await Cat("Servers", CategoryKind.Asset, 60);
            var furniture = await Cat("Furniture & Fixtures", CategoryKind.Asset, 120);
            var swOffice = await Cat("Productivity Software", CategoryKind.License, 0);
            var swSecurity = await Cat("Security Software", CategoryKind.License, 0);
            var accCat = await Cat("Peripherals", CategoryKind.Accessory, 0);
            var conCat = await Cat("Printer Supplies", CategoryKind.Consumable, 0);
            var compCat = await Cat("Memory & Storage", CategoryKind.Component, 0);

            var mans = new Dictionary<string, int>();
            foreach (var n in new[] { "Dell", "HP", "Lenovo", "Apple", "Samsung", "Zebra", "Cisco", "Microsoft", "Godrej", "Epson" })
                mans[n] = await Setup(new SetupItem { Kind = SetupKind.Manufacturer, Name = n, Url = $"https://www.{n.ToLowerInvariant()}.com" });
            var sups = new List<int>();
            foreach (var (n, c) in new[] { ("Redington India", "Sanjay Kapoor"), ("Ingram Micro India", "Meera Pillai"), ("Croma Business", "Arjun Rao"), ("Godrej Interio", "Farah Ali") })
                sups.Add(await Setup(new SetupItem { Kind = SetupKind.Supplier, Name = n, ContactName = c, Email = $"sales@{n.Split(' ')[0].ToLowerInvariant()}.example", Phone = $"+91 11 4000 {rnd.Next(1000, 9999)}" }));

            async Task<(int Id, string Name, int Cat, decimal Cost, string Prefix, int Warranty, string Number)> Model(string n, string no, int cat, string man, decimal cost, string prefix, int warranty) =>
                (await Setup(new SetupItem { Kind = SetupKind.Model, Name = n, ModelNumber = no, CategoryId = cat, ManufacturerId = mans[man], EolMonths = 60 }), n, cat, cost, prefix, warranty, no);
            var models = new[]
            {
                await Model("Latitude 5440", "LAT-5440", laptop, "Dell", 78000m, "LT", 36),
                await Model("ThinkPad T14 Gen 4", "21HD", laptop, "Lenovo", 92000m, "LT", 36),
                await Model("MacBook Pro 14 M3", "MRX33", laptop, "Apple", 189000m, "LT", 12),
                await Model("OptiPlex 7010", "OPT-7010", desktop, "Dell", 56000m, "DT", 36),
                await Model("ProDesk 400 G9", "6B2", desktop, "HP", 52000m, "DT", 36),
                await Model("TC52 Handheld", "TC52", mobile, "Zebra", 64000m, "HH", 24),
                await Model("Galaxy Tab Active4 Pro", "SM-T636", mobile, "Samsung", 58000m, "TB", 12),
                await Model("POS All-in-One TP-15", "TP15", pos, "HP", 115000m, "PO", 36),
                await Model("LaserJet Pro M404dn", "W1A53A", printer, "HP", 28000m, "PR", 12),
                await Model("ZD421 Label Printer", "ZD4A042", printer, "Zebra", 32000m, "PR", 24),
                await Model("Catalyst 9200 48-port", "C9200-48P", network, "Cisco", 245000m, "NW", 60),
                await Model("PowerEdge R760", "R760", server, "Dell", 640000m, "SV", 36),
                await Model("Ergonomic Chair", "GC-ERGO", furniture, "Godrej", 18000m, "FN", 12),
            };

            // ---- assets
            var plan = new (string Loc, int Count)[] { ("HO-GGN", 26), ("S045", 9), ("S046", 6), ("S012", 5), ("DC-NR1", 7), ("S210", 4), ("S330", 4) };
            var assets = new List<(int Id, string Loc, int Cat, int Model)>();
            var tagNo = 1000;
            foreach (var (loc, count) in plan)
            {
                for (var i = 0; i < count; i++)
                {
                    var mi = loc == "HO-GGN" ? rnd.Next(0, 5) switch { 4 => rnd.Next(10, 13), var k => k }
                        : loc.StartsWith("DC") ? new[] { 5, 9, 3, 10 }[rnd.Next(4)]
                        : new[] { 7, 7, 5, 6, 8, 9, 3 }[rnd.Next(7)];
                    var m = models[mi];
                    var bought = today.AddDays(-rnd.Next(60, 1300));
                    var dept = loc == "HO-GGN" ? new[] { "IT", "Finance", "HR", "Marketing" }[rnd.Next(4)] : loc.StartsWith("DC") ? "Supply Chain" : "Store Operations";
                    var res = await _assets.SaveAsync(w, new AssetSaveReq
                    {
                        AssetTag = $"VMM-{m.Prefix}-{++tagNo}", Name = m.Name, Serial = $"{m.Number}-{rnd.Next(100000, 999999)}", ModelId = m.Id, StatusLabelId = ready,
                        LocationId = locs[loc], DepartmentId = depts[dept], SupplierId = sups[rnd.Next(sups.Count)], PurchaseDate = bought,
                        PurchaseCost = Math.Round(m.Cost * (decimal)(0.9 + rnd.NextDouble() * 0.2) / 100m) * 100m, OrderNumber = $"PO-{bought:yyMM}-{rnd.Next(100, 999)}",
                        WarrantyMonths = m.Warranty, Requestable = m.Cat == laptop || m.Cat == mobile,
                    });
                    assets.Add((res.RecordId, loc, m.Cat, mi));
                }
            }

            // audits and warranty dates spread out so the dashboard has something to show
            await using (var conn = _db.CreateConnection())
            {
                foreach (var a in assets)
                {
                    var last = today.AddDays(-rnd.Next(30, 400));
                    await conn.ExecuteAsync("UPDATE dbo.Assets SET LastAuditDate = @last, NextAuditDate = @next WHERE Id = @id",
                        new { id = a.Id, last, next = last.AddMonths(12) });
                }
                foreach (var a in assets.Take(6))
                    await conn.ExecuteAsync("UPDATE dbo.Assets SET NextAuditDate = @next WHERE Id = @id", new { id = a.Id, next = today.AddDays(rnd.Next(-20, 25)) });
                foreach (var a in assets.Skip(10).Take(4))
                    await conn.ExecuteAsync("UPDATE dbo.Assets SET PurchaseDate = DATEADD(DAY, @d, DATEADD(MONTH, -WarrantyMonths, @today)) WHERE Id = @id",
                        new { id = a.Id, d = rnd.Next(10, 80), today });
            }

            // ---- people get their kit (history dated in the past)
            var holders = new Dictionary<string, string[]>
            {
                ["HO-GGN"] = new[] { "VMM10004", "VMM10005", "VMM10006", "VMM10008", "VMM10009", "VMM10010" },
                ["S045"] = new[] { "VMM10001", "VMM10002", "VMM10003" },
                ["S046"] = new[] { "VMM10001" },
                ["DC-NR1"] = new[] { "VMM10007" },
            };
            var issued = new HashSet<int>();
            foreach (var (loc, codes) in holders)
            {
                var pool = assets.Where(a => a.Loc == loc && a.Cat != network && a.Cat != server && a.Cat != pos).ToList();
                for (var i = 0; i < codes.Length && i < pool.Count; i++)
                {
                    await _assets.CheckoutAsync(w with { Now = now.AddDays(-rnd.Next(20, 300)) }, pool[i].Id, P(Who(codes[i])), null, null);
                    issued.Add(pool[i].Id);
                }
            }
            var loaner = assets.FirstOrDefault(a => a.Loc == "HO-GGN" && a.Cat == laptop && !issued.Contains(a.Id));
            if (loaner.Id != 0)
            {
                await _assets.CheckoutAsync(w with { Now = now.AddDays(-40) }, loaner.Id, P(Who("VMM10006")), today.AddDays(-5), "Loaner while own laptop is repaired");
                issued.Add(loaner.Id);
            }
            var spare = assets.Where(a => !issued.Contains(a.Id) && a.Cat != laptop).Take(3).ToList();
            async Task SetStatus(int id, int status) { await using var c = _db.CreateConnection(); await c.ExecuteAsync("UPDATE dbo.Assets SET StatusLabelId = @status WHERE Id = @id", new { id, status }); }
            if (spare.Count > 0) await SetStatus(spare[0].Id, repair);
            if (spare.Count > 1) await SetStatus(spare[1].Id, broken);
            if (spare.Count > 2) await SetStatus(spare[2].Id, retired);

            // ---- maintenance
            var wm = w with { Now = now.AddDays(-6) };
            if (spare.Count > 0)
                await _maintenance.SaveAsync(wm, new MaintenanceSaveReq { AssetId = spare[0].Id, MaintenanceType = MaintenanceType.Repair, Title = "Display flicker – panel replacement", SupplierId = sups[0], StartDate = today.AddDays(-6), Cost = 8500, IsWarranty = true });
            foreach (var a in assets.Where(a => a.Cat == pos).Take(3))
                await _maintenance.SaveAsync(w with { Now = now.AddDays(-39) }, new MaintenanceSaveReq { AssetId = a.Id, Title = "Quarterly POS cleaning & firmware update", SupplierId = sups[1], StartDate = today.AddDays(-40), CompletionDate = today.AddDays(-39), Cost = 1200 });
            var srv = assets.FirstOrDefault(a => a.Cat == server);
            if (srv.Id != 0)
                await _maintenance.SaveAsync(w with { Now = now.AddDays(-2) }, new MaintenanceSaveReq { AssetId = srv.Id, MaintenanceType = MaintenanceType.Upgrade, Title = "RAM upgrade to 512 GB", SupplierId = sups[0], StartDate = today.AddDays(-2), Cost = 145000 });

            // ---- licences
            async Task<int> Lic(string name, int seats, string man, int cat, decimal cost, int expiresInDays) => (await _licenses.SaveAsync(w, new LicenseSaveReq
            {
                Name = name, Seats = seats, ManufacturerId = mans[man], CategoryId = cat, SupplierId = sups[1], PurchaseCost = cost, PurchaseDate = today.AddDays(-300),
                ExpirationDate = today.AddDays(expiresInDays), ProductKey = $"{rnd.Next(10000, 99999)}-{rnd.Next(10000, 99999)}-{rnd.Next(10000, 99999)}-{rnd.Next(10000, 99999)}"
            })).RecordId;
            var m365 = await Lic("Microsoft 365 Business Premium", 25, "Microsoft", swOffice, 540000, 65);
            var win = await Lic("Windows 11 Pro (volume)", 40, "Microsoft", swOffice, 620000, 3000);
            var av = await Lic("Endpoint Protection Suite", 60, "Cisco", swSecurity, 360000, 200);
            await Lic("Adobe Acrobat Pro", 5, "HP", swOffice, 120000, 20);
            foreach (var p in People.Take(9)) await _licenses.CheckoutAsync(w, m365, P(p), null, null);
            foreach (var a in assets.Where(a => a.Cat == laptop || a.Cat == desktop).Take(18))
            {
                await _licenses.CheckoutAsync(w, win, null, a.Id, null);
                await _licenses.CheckoutAsync(w, av, null, a.Id, null);
            }

            // ---- accessories, consumables, components
            async Task<int> Stock(StockKind kind, string name, string man, int cat, string loc, int qty, int min, decimal cost) =>
                (await _stock.SaveAsync(w, new StockSaveReq { Kind = kind, Name = name, ManufacturerId = mans[man], CategoryId = cat, LocationId = locs[loc], Quantity = qty, MinQuantity = min, UnitCost = cost })).RecordId;
            var mouse = await Stock(StockKind.Accessory, "Wireless Mouse", "Dell", accCat, "HO-GGN", 40, 8, 900);
            var headset = await Stock(StockKind.Accessory, "USB Headset", "HP", accCat, "HO-GGN", 15, 4, 2400);
            var scanner = await Stock(StockKind.Accessory, "Barcode Scanner (USB)", "Zebra", accCat, "S045", 6, 2, 7800);
            await Stock(StockKind.Accessory, "Docking Station WD19", "Dell", accCat, "HO-GGN", 6, 3, 14500);
            foreach (var code in new[] { "VMM10004", "VMM10005", "VMM10008", "VMM10010" }) await _stock.CheckoutAsync(w, mouse, P(Who(code)), null, 1, null);
            await _stock.CheckoutAsync(w, headset, P(Who("VMM10006")), null, 1, null);
            foreach (var code in new[] { "VMM10002", "VMM10003", "VMM10001" }) await _stock.CheckoutAsync(w, scanner, P(Who(code)), null, 1, null);
            var toner = await Stock(StockKind.Consumable, "HP 76A Toner Cartridge", "HP", conCat, "HO-GGN", 10, 4, 6800);
            var rolls = await Stock(StockKind.Consumable, "Thermal Receipt Roll 79mm (box of 50)", "Epson", conCat, "S045", 7, 5, 1450);
            await Stock(StockKind.Consumable, "Shelf Label Roll (Zebra)", "Zebra", conCat, "DC-NR1", 24, 6, 650);
            await _stock.CheckoutAsync(w with { Now = now.AddDays(-12) }, toner, P(Who("VMM10006")), null, 1, null);
            await _stock.CheckoutAsync(w with { Now = now.AddDays(-3) }, rolls, P(Who("VMM10001")), null, 4, null);
            var ram = await Stock(StockKind.Component, "16 GB DDR5 SO-DIMM", "Samsung", compCat, "HO-GGN", 20, 4, 5200);
            await Stock(StockKind.Component, "1 TB NVMe SSD", "Samsung", compCat, "HO-GGN", 8, 2, 7400);
            foreach (var a in assets.Where(a => a.Cat == laptop).Take(3)) await _stock.CheckoutAsync(w, ram, null, a.Id, 1, null);

            // ---- requests
            var free = assets.Where(a => (a.Cat == laptop || a.Cat == mobile) && !issued.Contains(a.Id) && !spare.Any(s => s.Id == a.Id)).ToList();
            async Task Req(int i, string code, string reason, int daysAgo)
            {
                if (free.Count <= i) return;
                var who = Who(code);
                await _requests.CreateAsync(new WriteContext(tenant, now.AddDays(-daysAgo), who.Code, who.Name), free[i].Id, reason, who.Email);
            }
            await Req(0, "VMM10002", "Need a device for month-end stock reconciliation at S045.", 2);
            await Req(1, "VMM10010", "Spare laptop for vendor demo days.", 1);               // the approver's own request
            await Req(2, "VMM10004", "Current laptop battery fails within an hour.", 4);
            return true;
        }
    }
}
