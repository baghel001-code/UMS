namespace AMS.Domain.Enums
{
    public enum CategoryKind { Asset = 1, License = 2, Accessory = 3, Consumable = 4, Component = 5 }

    /// <summary>Deployable = ready to issue · Pending = not ready yet (e.g. in repair) · Undeployable = broken/lost · Archived = retired.</summary>
    public enum StatusType { Deployable = 1, Pending = 2, Undeployable = 3, Archived = 4 }

    public enum MaintenanceType { Maintenance = 1, Repair = 2, Upgrade = 3, Calibration = 4, Inspection = 5 }

    public enum RequestStatus { Pending = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

    /// <summary>Quantity-tracked items: accessories are lent and returned, consumables are used up, components go into assets.</summary>
    public enum StockKind { Accessory = 1, Consumable = 2, Component = 3 }

    public enum SetupKind { Category = 1, Manufacturer = 2, Model = 3, Supplier = 4, Location = 5, Department = 6, Status = 7 }

    public enum ItemType { Asset = 1, License = 2, Accessory = 3, Consumable = 4, Component = 5, Request = 6, Setup = 7 }

    public enum HistoryAction { Created = 1, Updated = 2, Deleted = 3, CheckedOut = 4, CheckedIn = 5, Audited = 6, Maintenance = 7, Requested = 8, Approved = 9, Rejected = 10, Cancelled = 11 }
}
