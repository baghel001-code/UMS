/*
  VMM Asset Management System (AMS) - schema (SQL Server 2019+)
  Database of its own (e.g. VMM_AMS). Every table carries TenantId (the UMS tenant) and every procedure filters on it.
  People are UMS users, referenced by employee code (EmpCode) - AMS stores no user accounts, roles or permissions.
  Enum columns store names (e.g. 'Deployable', 'Pending') for readability.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.Categories (
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
    TenantId           UNIQUEIDENTIFIER  NOT NULL,
    Name               NVARCHAR(120)     NOT NULL,
    Kind               NVARCHAR(20)      NOT NULL,      -- Asset | License | Accessory | Consumable | Component
    DepreciationMonths INT               NOT NULL CONSTRAINT DF_Categories_Dep DEFAULT (0),
    CONSTRAINT UQ_Categories UNIQUE (TenantId, Kind, Name)
);

CREATE TABLE dbo.Manufacturers (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Manufacturers PRIMARY KEY,
    TenantId     UNIQUEIDENTIFIER  NOT NULL,
    Name         NVARCHAR(120)     NOT NULL,
    Url          NVARCHAR(300)     NULL,
    SupportEmail NVARCHAR(200)     NULL,
    SupportPhone NVARCHAR(50)      NULL
);

CREATE TABLE dbo.Suppliers (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER  NOT NULL,
    Name        NVARCHAR(160)     NOT NULL,
    ContactName NVARCHAR(120)     NULL,
    Email       NVARCHAR(200)     NULL,
    Phone       NVARCHAR(50)      NULL
);

-- codes match UMS user locations (S045, DC-NR1, HO-GGN) so UMS role scopes such as {location: HO-GGN} apply
CREATE TABLE dbo.Locations (
    Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Locations PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER  NOT NULL,
    Code     NVARCHAR(30)      NOT NULL,
    Name     NVARCHAR(160)     NOT NULL,
    Region   NVARCHAR(30)      NOT NULL,
    Address  NVARCHAR(300)     NULL,
    CONSTRAINT UQ_Locations UNIQUE (TenantId, Code)
);

CREATE TABLE dbo.Departments (
    Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER  NOT NULL,
    Name     NVARCHAR(120)     NOT NULL
);

CREATE TABLE dbo.StatusLabels (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StatusLabels PRIMARY KEY,
    TenantId   UNIQUEIDENTIFIER  NOT NULL,
    Name       NVARCHAR(80)      NOT NULL,
    StatusType NVARCHAR(20)      NOT NULL     -- Deployable | Pending | Undeployable | Archived
);

CREATE TABLE dbo.AssetModels (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssetModels PRIMARY KEY,
    TenantId       UNIQUEIDENTIFIER  NOT NULL,
    Name           NVARCHAR(160)     NOT NULL,
    ModelNumber    NVARCHAR(80)      NULL,
    CategoryId     INT               NOT NULL CONSTRAINT FK_AssetModels_Category REFERENCES dbo.Categories (Id),
    ManufacturerId INT               NOT NULL CONSTRAINT FK_AssetModels_Manufacturer REFERENCES dbo.Manufacturers (Id),
    EolMonths      INT               NOT NULL CONSTRAINT DF_AssetModels_Eol DEFAULT (0)
);

CREATE TABLE dbo.Assets (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Assets PRIMARY KEY,
    TenantId          UNIQUEIDENTIFIER  NOT NULL,
    AssetTag          NVARCHAR(40)      NOT NULL,
    Name              NVARCHAR(160)     NOT NULL,
    Serial            NVARCHAR(100)     NULL,
    ModelId           INT               NOT NULL CONSTRAINT FK_Assets_Model REFERENCES dbo.AssetModels (Id),
    StatusLabelId     INT               NOT NULL CONSTRAINT FK_Assets_Status REFERENCES dbo.StatusLabels (Id),
    LocationId        INT               NOT NULL CONSTRAINT FK_Assets_Location REFERENCES dbo.Locations (Id),
    DepartmentId      INT               NULL CONSTRAINT FK_Assets_Department REFERENCES dbo.Departments (Id),
    SupplierId        INT               NULL CONSTRAINT FK_Assets_Supplier REFERENCES dbo.Suppliers (Id),
    PurchaseDate      DATE              NULL,
    PurchaseCost      DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Assets_Cost DEFAULT (0),
    OrderNumber       NVARCHAR(60)      NULL,
    WarrantyMonths    INT               NOT NULL CONSTRAINT DF_Assets_Warranty DEFAULT (0),
    Requestable       BIT               NOT NULL CONSTRAINT DF_Assets_Requestable DEFAULT (0),
    Notes             NVARCHAR(1000)    NULL,
    AssignedToEmpCode NVARCHAR(30)      NULL,          -- current holder (UMS employee code); NULL = in stock
    AssignedToName    NVARCHAR(200)     NULL,
    AssignedToEmail   NVARCHAR(200)     NULL,
    AssignedAt        DATETIME2         NULL,
    ExpectedCheckin   DATE              NULL,
    LastAuditDate     DATE              NULL,
    NextAuditDate     DATE              NULL,
    CreatedAt         DATETIME2         NOT NULL,
    UpdatedAt         DATETIME2         NOT NULL,
    CONSTRAINT UQ_Assets_Tag UNIQUE (TenantId, AssetTag)
);
CREATE INDEX IX_Assets_Holder ON dbo.Assets (TenantId, AssignedToEmpCode);

CREATE TABLE dbo.Maintenances (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Maintenances PRIMARY KEY,
    TenantId        UNIQUEIDENTIFIER  NOT NULL,
    AssetId         INT               NOT NULL CONSTRAINT FK_Maintenances_Asset REFERENCES dbo.Assets (Id),
    MaintenanceType NVARCHAR(20)      NOT NULL,      -- Maintenance | Repair | Upgrade | Calibration | Inspection
    Title           NVARCHAR(200)     NOT NULL,
    SupplierId      INT               NULL CONSTRAINT FK_Maintenances_Supplier REFERENCES dbo.Suppliers (Id),
    StartDate       DATE              NOT NULL,
    CompletionDate  DATE              NULL,
    Cost            DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Maintenances_Cost DEFAULT (0),
    IsWarranty      BIT               NOT NULL CONSTRAINT DF_Maintenances_Warranty DEFAULT (0),
    Notes           NVARCHAR(1000)    NULL
);

CREATE TABLE dbo.Licenses (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Licenses PRIMARY KEY,
    TenantId       UNIQUEIDENTIFIER  NOT NULL,
    Name           NVARCHAR(200)     NOT NULL,
    ProductKey     NVARCHAR(500)     NULL,
    Seats          INT               NOT NULL,
    ManufacturerId INT               NULL CONSTRAINT FK_Licenses_Manufacturer REFERENCES dbo.Manufacturers (Id),
    CategoryId     INT               NULL CONSTRAINT FK_Licenses_Category REFERENCES dbo.Categories (Id),
    SupplierId     INT               NULL CONSTRAINT FK_Licenses_Supplier REFERENCES dbo.Suppliers (Id),
    PurchaseDate   DATE              NULL,
    PurchaseCost   DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Licenses_Cost DEFAULT (0),
    ExpirationDate DATE              NULL,
    Notes          NVARCHAR(1000)    NULL
);

-- one used seat: given to a person and/or installed on an asset
CREATE TABLE dbo.LicenseSeats (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LicenseSeats PRIMARY KEY,
    TenantId          UNIQUEIDENTIFIER  NOT NULL,
    LicenseId         INT               NOT NULL CONSTRAINT FK_LicenseSeats_License REFERENCES dbo.Licenses (Id) ON DELETE CASCADE,
    AssignedToEmpCode NVARCHAR(30)      NULL,
    AssignedToName    NVARCHAR(200)     NULL,
    AssetId           INT               NULL CONSTRAINT FK_LicenseSeats_Asset REFERENCES dbo.Assets (Id),
    AssignedAt        DATETIME2         NOT NULL
);

-- accessories (lent and returned), consumables (issued, used up) and components (installed into assets)
CREATE TABLE dbo.StockItems (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockItems PRIMARY KEY,
    TenantId       UNIQUEIDENTIFIER  NOT NULL,
    Kind           NVARCHAR(20)      NOT NULL,      -- Accessory | Consumable | Component
    Name           NVARCHAR(200)     NOT NULL,
    CategoryId     INT               NULL CONSTRAINT FK_StockItems_Category REFERENCES dbo.Categories (Id),
    ManufacturerId INT               NULL CONSTRAINT FK_StockItems_Manufacturer REFERENCES dbo.Manufacturers (Id),
    LocationId     INT               NOT NULL CONSTRAINT FK_StockItems_Location REFERENCES dbo.Locations (Id),
    ModelNumber    NVARCHAR(80)      NULL,
    Serial         NVARCHAR(100)     NULL,
    Quantity       INT               NOT NULL,      -- total owned (consumables: remaining)
    MinQuantity    INT               NOT NULL CONSTRAINT DF_StockItems_Min DEFAULT (0),
    UnitCost       DECIMAL(18,2)     NOT NULL CONSTRAINT DF_StockItems_Cost DEFAULT (0),
    Notes          NVARCHAR(1000)    NULL
);

-- accessory with a person / consumable issued to a person (history) / component installed in an asset
CREATE TABLE dbo.StockAssignments (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockAssignments PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER  NOT NULL,
    StockItemId INT               NOT NULL CONSTRAINT FK_StockAssignments_Item REFERENCES dbo.StockItems (Id) ON DELETE CASCADE,
    EmpCode     NVARCHAR(30)      NULL,
    EmpName     NVARCHAR(200)     NULL,
    AssetId     INT               NULL CONSTRAINT FK_StockAssignments_Asset REFERENCES dbo.Assets (Id),
    Quantity    INT               NOT NULL,
    AssignedAt  DATETIME2         NOT NULL
);

CREATE TABLE dbo.AssetRequests (
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssetRequests PRIMARY KEY,
    TenantId           UNIQUEIDENTIFIER  NOT NULL,
    AssetId            INT               NOT NULL CONSTRAINT FK_AssetRequests_Asset REFERENCES dbo.Assets (Id),
    RequestedByEmpCode NVARCHAR(30)      NOT NULL,
    RequestedByName    NVARCHAR(200)     NOT NULL,
    RequestedByEmail   NVARCHAR(200)     NULL,
    Reason             NVARCHAR(1000)    NOT NULL,
    Status             NVARCHAR(20)      NOT NULL,      -- Pending | Approved | Rejected | Cancelled
    RequestedAt        DATETIME2         NOT NULL,
    DecidedByEmpCode   NVARCHAR(30)      NULL,
    DecidedByName      NVARCHAR(200)     NULL,
    DecidedAt          DATETIME2         NULL,
    DecisionNote       NVARCHAR(1000)    NULL
);

-- activity log: who did what to which item (UMS keeps the permission decisions in its user action trail)
CREATE TABLE dbo.History (
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_History PRIMARY KEY NONCLUSTERED,
    TenantId     UNIQUEIDENTIFIER     NOT NULL,
    At           DATETIME2            NOT NULL,
    ActorEmpCode NVARCHAR(30)         NOT NULL,
    ActorName    NVARCHAR(200)        NOT NULL,
    ItemType     NVARCHAR(20)         NOT NULL,      -- Asset | License | Accessory | Consumable | Component | Request | Setup
    ItemId       INT                  NOT NULL,
    ItemName     NVARCHAR(300)        NOT NULL,
    LocationCode NVARCHAR(30)         NULL,
    Action       NVARCHAR(20)         NOT NULL,      -- Created | Updated | Deleted | CheckedOut | CheckedIn | Audited | ...
    Target       NVARCHAR(300)        NULL,
    Note         NVARCHAR(1000)       NULL
);
CREATE CLUSTERED INDEX CIX_History ON dbo.History (TenantId, At DESC);
CREATE INDEX IX_History_Item ON dbo.History (TenantId, ItemType, ItemId);
GO
