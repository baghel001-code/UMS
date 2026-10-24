using AMS.Application.Interfaces;
using AMS.Domain.Enums;
using AMS.Domain.Models;
using AMS.Domain.Security;

namespace AMS.Tests
{
    /// <summary>Records shaped like the UMS demo tenant (Priya at store S045, Sneha at head office HO-GGN).</summary>
    internal static class TestData
    {
        public static readonly Guid Tenant = Guid.Parse("9c931e4a-cf70-bb5b-0d66-217df75d70aa");
        public static readonly Actor Rahul = new(Guid.NewGuid(), Tenant, "VMM10010", "Rahul Agarwal", "rahul.agarwal@vmm.example");

        public static readonly Person Priya = new("VMM10002", "Priya Verma", "priya.verma@vmm.example", "Store Operations", "Store Executive", "S045", "North",
            "Permanent", "Active");

        public static readonly Person Imran = new("VMM10007", "Imran Khan", "imran.khan@vmm.example", "Warehouse", "Picker", "DC-NR1", "North", "Contract", "Active");

        public static AssetView Laptop(int id = 7, string? assignedTo = null) => new()
        {
            Id = id, AssetTag = $"VMM-LT-{1000 + id}", Name = "MacBook Pro 14", ModelId = 1, ModelName = "MacBook Pro 14", CategoryId = 1, CategoryName = "Laptops",
            DepreciationMonths = 36, ManufacturerName = "Apple", StatusLabelId = 1, StatusName = "Ready to Deploy", StatusType = StatusType.Deployable,
            LocationId = 1, LocationCode = "HO-GGN", LocationName = "Head Office, Gurugram", Region = "North", PurchaseCost = 210000m,
            PurchaseDate = new DateTime(2025, 4, 1), WarrantyMonths = 12, AssignedToEmpCode = assignedTo
        };

        public static RequestView PendingRequest(string by = "VMM10002") => new()
        {
            Id = 3, AssetId = 7, RequestedByEmpCode = by, RequestedByName = "Priya Verma", Reason = "Store audits", Status = RequestStatus.Pending,
            AssetTag = "VMM-LT-1007", AssetName = "MacBook Pro 14", AssetCost = 210000m, LocationCode = "HO-GGN", Region = "North", CategoryName = "Laptops"
        };

        public static ICurrentActor ActorOf(Actor actor)
        {
            var current = Substitute.For<ICurrentActor>();
            current.Actor.Returns(actor);
            current.AccessToken.Returns("user-token");
            current.ClientIp.Returns("10.20.30.40");
            return current;
        }

        public static IBusinessClock Clock(DateTime now)
        {
            var clock = Substitute.For<IBusinessClock>();
            clock.Now.Returns(now);
            clock.Today.Returns(now.Date);
            return clock;
        }
    }
}
