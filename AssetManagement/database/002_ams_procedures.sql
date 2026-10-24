/*
  VMM Asset Management System (AMS) - views and stored procedures (SQL Server 2019+). Safe to re-run (CREATE OR ALTER).

  Conventions (as in the other VMM applications):
    * every procedure takes @TenantId and only touches that tenant's rows;
    * write procedures return @Error_Code / @Error_Message (0 = success) and the affected id in @Record_Id;
      1001 = not found, 1002 = business rule broken, 1003 = duplicate;
    * @Now is the business wall-clock time (IST) passed by the API, @ActorEmpCode/@ActorName the signed-in UMS user.
  Access control is NOT done here: the AMS API asks UMS before calling any procedure.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ============================================================== views

CREATE OR ALTER VIEW dbo.vw_Assets AS
SELECT a.Id, a.TenantId, a.AssetTag, a.Name, a.Serial, a.ModelId, m.Name AS ModelName, m.ModelNumber, m.CategoryId, c.Name AS CategoryName,
       c.DepreciationMonths, m.ManufacturerId, mf.Name AS ManufacturerName, a.StatusLabelId, s.Name AS StatusName, s.StatusType,
       a.LocationId, l.Code AS LocationCode, l.Name AS LocationName, l.Region, a.DepartmentId, d.Name AS DepartmentName,
       a.SupplierId, sp.Name AS SupplierName, a.PurchaseDate, a.PurchaseCost, a.OrderNumber, a.WarrantyMonths, a.Requestable, a.Notes,
       a.AssignedToEmpCode, a.AssignedToName, a.AssignedToEmail, a.AssignedAt, a.ExpectedCheckin, a.LastAuditDate, a.NextAuditDate, a.CreatedAt, a.UpdatedAt
FROM dbo.Assets a
JOIN dbo.AssetModels m ON m.Id = a.ModelId
JOIN dbo.Categories c ON c.Id = m.CategoryId
JOIN dbo.Manufacturers mf ON mf.Id = m.ManufacturerId
JOIN dbo.StatusLabels s ON s.Id = a.StatusLabelId
JOIN dbo.Locations l ON l.Id = a.LocationId
LEFT JOIN dbo.Departments d ON d.Id = a.DepartmentId
LEFT JOIN dbo.Suppliers sp ON sp.Id = a.SupplierId;
GO

-- ============================================================== history

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_HISTORY_ADD
    @TenantId UNIQUEIDENTIFIER, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @ItemType NVARCHAR(20), @ItemId INT, @ItemName NVARCHAR(300), @LocationCode NVARCHAR(30) = NULL,
    @Action NVARCHAR(20), @Target NVARCHAR(300) = NULL, @Note NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.History (TenantId, At, ActorEmpCode, ActorName, ItemType, ItemId, ItemName, LocationCode, Action, Target, Note)
    VALUES (@TenantId, @Now, @ActorEmpCode, @ActorName, @ItemType, @ItemId, @ItemName, @LocationCode, @Action, @Target, NULLIF(LTRIM(RTRIM(@Note)), ''));
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_HISTORY_LIST
    @TenantId UNIQUEIDENTIFIER, @ItemType NVARCHAR(20) = NULL, @ItemId INT = NULL, @Search NVARCHAR(100) = NULL,
    @From DATE = NULL, @To DATE = NULL, @Take INT = 2000
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Take) Id, At, ActorEmpCode, ActorName, ItemType, ItemId, ItemName, LocationCode, Action, Target, Note
    FROM dbo.History
    WHERE TenantId = @TenantId
      AND (@ItemType IS NULL OR ItemType = @ItemType)
      AND (@ItemId IS NULL OR ItemId = @ItemId)
      AND (@From IS NULL OR At >= @From)
      AND (@To IS NULL OR At < DATEADD(DAY, 1, @To))
      AND (@Search IS NULL OR ItemName LIKE '%' + @Search + '%' OR ActorName LIKE '%' + @Search + '%' OR ActorEmpCode LIKE '%' + @Search + '%'
           OR Target LIKE '%' + @Search + '%' OR Note LIKE '%' + @Search + '%')
    ORDER BY At DESC, Id DESC;
END
GO

-- ============================================================== setup (categories, manufacturers, models, suppliers, locations, departments, statuses)

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_SETUP_LIST
    @TenantId UNIQUEIDENTIFIER, @Kind NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kind = 'Category'
        SELECT c.Id, 'Category' AS Kind, c.Name, c.Kind AS CategoryKind, c.DepreciationMonths,
               (SELECT COUNT(*) FROM dbo.AssetModels m WHERE m.CategoryId = c.Id) + (SELECT COUNT(*) FROM dbo.Licenses x WHERE x.CategoryId = c.Id)
             + (SELECT COUNT(*) FROM dbo.StockItems x WHERE x.CategoryId = c.Id) AS UsedBy
        FROM dbo.Categories c WHERE c.TenantId = @TenantId ORDER BY c.Kind, c.Name;
    ELSE IF @Kind = 'Manufacturer'
        SELECT f.Id, 'Manufacturer' AS Kind, f.Name, f.Url, f.SupportEmail AS Email, f.SupportPhone AS Phone,
               (SELECT COUNT(*) FROM dbo.AssetModels m WHERE m.ManufacturerId = f.Id) + (SELECT COUNT(*) FROM dbo.Licenses x WHERE x.ManufacturerId = f.Id)
             + (SELECT COUNT(*) FROM dbo.StockItems x WHERE x.ManufacturerId = f.Id) AS UsedBy
        FROM dbo.Manufacturers f WHERE f.TenantId = @TenantId ORDER BY f.Name;
    ELSE IF @Kind = 'Model'
        SELECT m.Id, 'Model' AS Kind, m.Name, m.ModelNumber, m.CategoryId, c.Name AS CategoryName, m.ManufacturerId, f.Name AS ManufacturerName, m.EolMonths,
               (SELECT COUNT(*) FROM dbo.Assets a WHERE a.ModelId = m.Id) AS UsedBy
        FROM dbo.AssetModels m JOIN dbo.Categories c ON c.Id = m.CategoryId JOIN dbo.Manufacturers f ON f.Id = m.ManufacturerId
        WHERE m.TenantId = @TenantId ORDER BY m.Name;
    ELSE IF @Kind = 'Supplier'
        SELECT s.Id, 'Supplier' AS Kind, s.Name, s.ContactName, s.Email, s.Phone,
               (SELECT COUNT(*) FROM dbo.Assets a WHERE a.SupplierId = s.Id) + (SELECT COUNT(*) FROM dbo.Licenses x WHERE x.SupplierId = s.Id)
             + (SELECT COUNT(*) FROM dbo.Maintenances x WHERE x.SupplierId = s.Id) AS UsedBy
        FROM dbo.Suppliers s WHERE s.TenantId = @TenantId ORDER BY s.Name;
    ELSE IF @Kind = 'Location'
        SELECT l.Id, 'Location' AS Kind, l.Name, l.Code, l.Region, l.Address,
               (SELECT COUNT(*) FROM dbo.Assets a WHERE a.LocationId = l.Id) + (SELECT COUNT(*) FROM dbo.StockItems x WHERE x.LocationId = l.Id) AS UsedBy
        FROM dbo.Locations l WHERE l.TenantId = @TenantId ORDER BY l.Code;
    ELSE IF @Kind = 'Department'
        SELECT d.Id, 'Department' AS Kind, d.Name, (SELECT COUNT(*) FROM dbo.Assets a WHERE a.DepartmentId = d.Id) AS UsedBy
        FROM dbo.Departments d WHERE d.TenantId = @TenantId ORDER BY d.Name;
    ELSE IF @Kind = 'Status'
        SELECT s.Id, 'Status' AS Kind, s.Name, s.StatusType, (SELECT COUNT(*) FROM dbo.Assets a WHERE a.StatusLabelId = s.Id) AS UsedBy
        FROM dbo.StatusLabels s WHERE s.TenantId = @TenantId ORDER BY s.StatusType, s.Name;
END
GO

-- all drop-down lists in one call (7 result sets)
CREATE OR ALTER PROCEDURE dbo.PRO_AMS_OPTIONS
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, 'Category' AS Kind, Name, Kind AS CategoryKind, DepreciationMonths FROM dbo.Categories WHERE TenantId = @TenantId ORDER BY Name;
    SELECT Id, 'Manufacturer' AS Kind, Name FROM dbo.Manufacturers WHERE TenantId = @TenantId ORDER BY Name;
    SELECT m.Id, 'Model' AS Kind, m.Name, m.ModelNumber, m.CategoryId, c.Name AS CategoryName, c.Kind AS CategoryKind, m.ManufacturerId, f.Name AS ManufacturerName
    FROM dbo.AssetModels m JOIN dbo.Categories c ON c.Id = m.CategoryId JOIN dbo.Manufacturers f ON f.Id = m.ManufacturerId
    WHERE m.TenantId = @TenantId ORDER BY m.Name;
    SELECT Id, 'Supplier' AS Kind, Name FROM dbo.Suppliers WHERE TenantId = @TenantId ORDER BY Name;
    SELECT Id, 'Location' AS Kind, Name, Code, Region FROM dbo.Locations WHERE TenantId = @TenantId ORDER BY Code;
    SELECT Id, 'Department' AS Kind, Name FROM dbo.Departments WHERE TenantId = @TenantId ORDER BY Name;
    SELECT Id, 'Status' AS Kind, Name, StatusType FROM dbo.StatusLabels WHERE TenantId = @TenantId ORDER BY StatusType, Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_SETUP_SAVE
    @TenantId UNIQUEIDENTIFIER, @Kind NVARCHAR(20), @Id INT = 0, @Name NVARCHAR(200),
    @Code NVARCHAR(30) = NULL, @Region NVARCHAR(30) = NULL, @Address NVARCHAR(300) = NULL,
    @CategoryKind NVARCHAR(20) = NULL, @DepreciationMonths INT = 0, @StatusType NVARCHAR(20) = NULL,
    @Url NVARCHAR(300) = NULL, @ContactName NVARCHAR(120) = NULL, @Email NVARCHAR(200) = NULL, @Phone NVARCHAR(50) = NULL,
    @ModelNumber NVARCHAR(80) = NULL, @CategoryId INT = NULL, @ManufacturerId INT = NULL, @EolMonths INT = 0,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS', @Record_Id = NULLIF(@Id, 0), @Name = LTRIM(RTRIM(@Name));
    IF ISNULL(@Name, '') = '' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Name is required.'; RETURN; END

    DECLARE @exists BIT = 0;
    IF @Kind = 'Category'
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.Categories WHERE TenantId = @TenantId AND Kind = @CategoryKind AND Name = @Name AND Id <> ISNULL(@Record_Id, 0))
        BEGIN SELECT @Error_Code = 1003, @Error_Message = CONCAT('Category ''', @Name, ''' already exists.'); RETURN; END
        IF @Record_Id IS NULL
        BEGIN INSERT INTO dbo.Categories (TenantId, Name, Kind, DepreciationMonths) VALUES (@TenantId, @Name, @CategoryKind, ISNULL(@DepreciationMonths, 0)); SET @Record_Id = SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Categories SET Name = @Name, Kind = @CategoryKind, DepreciationMonths = ISNULL(@DepreciationMonths, 0) WHERE Id = @Record_Id AND TenantId = @TenantId;
        SET @exists = @@ROWCOUNT;
    END
    ELSE IF @Kind = 'Manufacturer'
    BEGIN
        IF @Record_Id IS NULL
        BEGIN INSERT INTO dbo.Manufacturers (TenantId, Name, Url, SupportEmail, SupportPhone) VALUES (@TenantId, @Name, @Url, @Email, @Phone); SET @Record_Id = SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Manufacturers SET Name = @Name, Url = @Url, SupportEmail = @Email, SupportPhone = @Phone WHERE Id = @Record_Id AND TenantId = @TenantId;
        SET @exists = @@ROWCOUNT;
    END
    ELSE IF @Kind = 'Model'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = @CategoryId AND TenantId = @TenantId)
           OR NOT EXISTS (SELECT 1 FROM dbo.Manufacturers WHERE Id = @ManufacturerId AND TenantId = @TenantId)
        BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Choose a category and a manufacturer.'; RETURN; END
        IF @Record_Id IS NULL
        BEGIN INSERT INTO dbo.AssetModels (TenantId, Name, ModelNumber, CategoryId, ManufacturerId, EolMonths) VALUES (@TenantId, @Name, @ModelNumber, @CategoryId, @ManufacturerId, ISNULL(@EolMonths, 0)); SET @Record_Id = SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.AssetModels SET Name = @Name, ModelNumber = @ModelNumber, CategoryId = @CategoryId, ManufacturerId = @ManufacturerId, EolMonths = ISNULL(@EolMonths, 0) WHERE Id = @Record_Id AND TenantId = @TenantId;
        SET @exists = @@ROWCOUNT;
    END
    ELSE IF @Kind = 'Supplier'
    BEGIN
        IF @Record_Id IS NULL
        BEGIN INSERT INTO dbo.Suppliers (TenantId, Name, ContactName, Email, Phone) VALUES (@TenantId, @Name, @ContactName, @Email, @Phone); SET @Record_Id = SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Suppliers SET Name = @Name, ContactName = @ContactName, Email = @Email, Phone = @Phone WHERE Id = @Record_Id AND TenantId = @TenantId;
        SET @exists = @@ROWCOUNT;
    END
    ELSE IF @Kind = 'Location'
    BEGIN
        SET @Code = UPPER(LTRIM(RTRIM(@Code)));
        IF ISNULL(@Code, '') = '' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Code is required (e.g. S045, DC-NR1, HO-GGN).'; RETURN; END
        IF EXISTS (SELECT 1 FROM dbo.Locations WHERE TenantId = @TenantId AND Code = @Code AND Id <> ISNULL(@Record_Id, 0))
        BEGIN SELECT @Error_Code = 1003, @Error_Message = CONCAT('Location code ', @Code, ' already exists.'); RETURN; END
        IF @Record_Id IS NULL
        BEGIN INSERT INTO dbo.Locations (TenantId, Code, Name, Region, Address) VALUES (@TenantId, @Code, @Name, ISNULL(@Region, ''), @Address); SET @Record_Id = SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Locations SET Code = @Code, Name = @Name, Region = ISNULL(@Region, ''), Address = @Address WHERE Id = @Record_Id AND TenantId = @TenantId;
        SET @exists = @@ROWCOUNT;
    END
    ELSE IF @Kind = 'Department'
    BEGIN
        IF @Record_Id IS NULL
        BEGIN INSERT INTO dbo.Departments (TenantId, Name) VALUES (@TenantId, @Name); SET @Record_Id = SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Departments SET Name = @Name WHERE Id = @Record_Id AND TenantId = @TenantId;
        SET @exists = @@ROWCOUNT;
    END
    ELSE IF @Kind = 'Status'
    BEGIN
        IF @Record_Id IS NULL
        BEGIN INSERT INTO dbo.StatusLabels (TenantId, Name, StatusType) VALUES (@TenantId, @Name, ISNULL(@StatusType, 'Deployable')); SET @Record_Id = SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.StatusLabels SET Name = @Name, StatusType = ISNULL(@StatusType, 'Deployable') WHERE Id = @Record_Id AND TenantId = @TenantId;
        SET @exists = @@ROWCOUNT;
    END
    ELSE BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT('Unknown setup list ', @Kind, '.'); RETURN; END

    IF @exists = 0 BEGIN SELECT @Error_Code = 1001, @Error_Message = CONCAT(@Kind, ' not found.'); RETURN; END
    DECLARE @item NVARCHAR(300) = CONCAT(@Kind, ': ', @Name), @act NVARCHAR(20) = CASE WHEN @Id > 0 THEN 'Updated' ELSE 'Created' END;
    DECLARE @loc NVARCHAR(30) = CASE WHEN @Kind = 'Location' THEN @Code END;
    EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Setup', @Record_Id, @item, @loc, @act;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_SETUP_DELETE
    @TenantId UNIQUEIDENTIFIER, @Kind NVARCHAR(20), @Id INT, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @used INT = 0, @name NVARCHAR(200), @code NVARCHAR(30);
    IF @Kind = 'Category'
        SELECT @name = Name, @used = (SELECT COUNT(*) FROM dbo.AssetModels WHERE CategoryId = @Id) + (SELECT COUNT(*) FROM dbo.Licenses WHERE CategoryId = @Id) + (SELECT COUNT(*) FROM dbo.StockItems WHERE CategoryId = @Id)
        FROM dbo.Categories WHERE Id = @Id AND TenantId = @TenantId;
    ELSE IF @Kind = 'Manufacturer'
        SELECT @name = Name, @used = (SELECT COUNT(*) FROM dbo.AssetModels WHERE ManufacturerId = @Id) + (SELECT COUNT(*) FROM dbo.Licenses WHERE ManufacturerId = @Id) + (SELECT COUNT(*) FROM dbo.StockItems WHERE ManufacturerId = @Id)
        FROM dbo.Manufacturers WHERE Id = @Id AND TenantId = @TenantId;
    ELSE IF @Kind = 'Model'
        SELECT @name = Name, @used = (SELECT COUNT(*) FROM dbo.Assets WHERE ModelId = @Id) FROM dbo.AssetModels WHERE Id = @Id AND TenantId = @TenantId;
    ELSE IF @Kind = 'Supplier'
        SELECT @name = Name, @used = (SELECT COUNT(*) FROM dbo.Assets WHERE SupplierId = @Id) + (SELECT COUNT(*) FROM dbo.Licenses WHERE SupplierId = @Id) + (SELECT COUNT(*) FROM dbo.Maintenances WHERE SupplierId = @Id)
        FROM dbo.Suppliers WHERE Id = @Id AND TenantId = @TenantId;
    ELSE IF @Kind = 'Location'
        SELECT @name = Name, @code = Code, @used = (SELECT COUNT(*) FROM dbo.Assets WHERE LocationId = @Id) + (SELECT COUNT(*) FROM dbo.StockItems WHERE LocationId = @Id)
        FROM dbo.Locations WHERE Id = @Id AND TenantId = @TenantId;
    ELSE IF @Kind = 'Department'
        SELECT @name = Name, @used = (SELECT COUNT(*) FROM dbo.Assets WHERE DepartmentId = @Id) FROM dbo.Departments WHERE Id = @Id AND TenantId = @TenantId;
    ELSE IF @Kind = 'Status'
        SELECT @name = Name, @used = (SELECT COUNT(*) FROM dbo.Assets WHERE StatusLabelId = @Id) FROM dbo.StatusLabels WHERE Id = @Id AND TenantId = @TenantId;

    IF @name IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = CONCAT(@Kind, ' not found.'); RETURN; END
    IF @used > 0 BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@name, ' is used by ', @used, ' record(s).'); RETURN; END

    IF @Kind = 'Category' DELETE dbo.Categories WHERE Id = @Id;
    ELSE IF @Kind = 'Manufacturer' DELETE dbo.Manufacturers WHERE Id = @Id;
    ELSE IF @Kind = 'Model' DELETE dbo.AssetModels WHERE Id = @Id;
    ELSE IF @Kind = 'Supplier' DELETE dbo.Suppliers WHERE Id = @Id;
    ELSE IF @Kind = 'Location' DELETE dbo.Locations WHERE Id = @Id;
    ELSE IF @Kind = 'Department' DELETE dbo.Departments WHERE Id = @Id;
    ELSE IF @Kind = 'Status' DELETE dbo.StatusLabels WHERE Id = @Id;
    DECLARE @item NVARCHAR(300) = CONCAT(@Kind, ': ', @name);
    EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Setup', @Id, @item, @code, 'Deleted';
END
GO

-- ============================================================== assets

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_ASSET_LIST
    @TenantId UNIQUEIDENTIFIER, @Id INT = NULL, @Search NVARCHAR(100) = NULL, @CategoryId INT = NULL, @LocationId INT = NULL,
    @StatusLabelId INT = NULL, @CheckedOut BIT = NULL, @AssignedToEmpCode NVARCHAR(30) = NULL, @RequestableOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.vw_Assets
    WHERE TenantId = @TenantId
      AND (@Id IS NULL OR Id = @Id)
      AND (@CategoryId IS NULL OR CategoryId = @CategoryId)
      AND (@LocationId IS NULL OR LocationId = @LocationId)
      AND (@StatusLabelId IS NULL OR StatusLabelId = @StatusLabelId)
      AND (@CheckedOut IS NULL OR (@CheckedOut = 1 AND AssignedToEmpCode IS NOT NULL) OR (@CheckedOut = 0 AND AssignedToEmpCode IS NULL))
      AND (@AssignedToEmpCode IS NULL OR AssignedToEmpCode = @AssignedToEmpCode)
      AND (@RequestableOnly = 0 OR (Requestable = 1 AND AssignedToEmpCode IS NULL AND StatusType = 'Deployable'))
      AND (@Search IS NULL OR AssetTag LIKE '%' + @Search + '%' OR Name LIKE '%' + @Search + '%' OR Serial LIKE '%' + @Search + '%'
           OR ModelName LIKE '%' + @Search + '%' OR AssignedToName LIKE '%' + @Search + '%' OR AssignedToEmpCode LIKE '%' + @Search + '%')
    ORDER BY AssetTag;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_ASSET_SAVE
    @TenantId UNIQUEIDENTIFIER, @Id INT = 0, @AssetTag NVARCHAR(40), @Name NVARCHAR(160), @Serial NVARCHAR(100) = NULL,
    @ModelId INT, @StatusLabelId INT, @LocationId INT, @DepartmentId INT = NULL, @SupplierId INT = NULL,
    @PurchaseDate DATE = NULL, @PurchaseCost DECIMAL(18,2) = 0, @OrderNumber NVARCHAR(60) = NULL, @WarrantyMonths INT = 0,
    @Requestable BIT = 0, @Notes NVARCHAR(1000) = NULL, @NextAuditDate DATE = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS', @Record_Id = NULLIF(@Id, 0),
           @AssetTag = UPPER(LTRIM(RTRIM(@AssetTag))), @Name = LTRIM(RTRIM(@Name)), @Serial = NULLIF(LTRIM(RTRIM(@Serial)), '');
    IF ISNULL(@AssetTag, '') = '' OR ISNULL(@Name, '') = '' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Asset tag and name are required.'; RETURN; END
    IF @PurchaseCost < 0 BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Cost cannot be negative.'; RETURN; END
    IF EXISTS (SELECT 1 FROM dbo.Assets WHERE TenantId = @TenantId AND AssetTag = @AssetTag AND Id <> ISNULL(@Record_Id, 0))
    BEGIN SELECT @Error_Code = 1003, @Error_Message = CONCAT('Asset tag ', @AssetTag, ' is already used.'); RETURN; END
    IF NOT EXISTS (SELECT 1 FROM dbo.AssetModels WHERE Id = @ModelId AND TenantId = @TenantId)
       OR NOT EXISTS (SELECT 1 FROM dbo.StatusLabels WHERE Id = @StatusLabelId AND TenantId = @TenantId)
       OR NOT EXISTS (SELECT 1 FROM dbo.Locations WHERE Id = @LocationId AND TenantId = @TenantId)
       OR (@DepartmentId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE Id = @DepartmentId AND TenantId = @TenantId))
       OR (@SupplierId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE Id = @SupplierId AND TenantId = @TenantId))
    BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Choose a valid model, status and location.'; RETURN; END

    IF @Record_Id IS NULL
    BEGIN
        INSERT INTO dbo.Assets (TenantId, AssetTag, Name, Serial, ModelId, StatusLabelId, LocationId, DepartmentId, SupplierId, PurchaseDate, PurchaseCost,
                                OrderNumber, WarrantyMonths, Requestable, Notes, LastAuditDate, NextAuditDate, CreatedAt, UpdatedAt)
        VALUES (@TenantId, @AssetTag, @Name, @Serial, @ModelId, @StatusLabelId, @LocationId, @DepartmentId, @SupplierId, @PurchaseDate, @PurchaseCost,
                @OrderNumber, ISNULL(@WarrantyMonths, 0), @Requestable, @Notes, NULL, ISNULL(@NextAuditDate, DATEADD(MONTH, 12, CAST(@Now AS DATE))), @Now, @Now);
        SET @Record_Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.Assets SET AssetTag = @AssetTag, Name = @Name, Serial = @Serial, ModelId = @ModelId, StatusLabelId = @StatusLabelId, LocationId = @LocationId,
               DepartmentId = @DepartmentId, SupplierId = @SupplierId, PurchaseDate = @PurchaseDate, PurchaseCost = @PurchaseCost, OrderNumber = @OrderNumber,
               WarrantyMonths = ISNULL(@WarrantyMonths, 0), Requestable = @Requestable, Notes = @Notes, NextAuditDate = ISNULL(@NextAuditDate, NextAuditDate), UpdatedAt = @Now
        WHERE Id = @Record_Id AND TenantId = @TenantId;
        IF @@ROWCOUNT = 0 BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    END

    DECLARE @item NVARCHAR(300) = CONCAT(@AssetTag, ' ', @Name), @loc NVARCHAR(30) = (SELECT Code FROM dbo.Locations WHERE Id = @LocationId),
            @act NVARCHAR(20) = CASE WHEN @Id > 0 THEN 'Updated' ELSE 'Created' END;
    EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @Record_Id, @item, @loc, @act;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_ASSET_DELETE
    @TenantId UNIQUEIDENTIFIER, @Id INT, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @item NVARCHAR(300), @loc NVARCHAR(30), @holder NVARCHAR(30);
    SELECT @item = CONCAT(AssetTag, ' ', Name), @loc = LocationCode, @holder = AssignedToEmpCode FROM dbo.vw_Assets WHERE Id = @Id AND TenantId = @TenantId;
    IF @item IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    IF @holder IS NOT NULL BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Check the asset in before deleting it.'; RETURN; END
    BEGIN TRANSACTION;
        DELETE dbo.LicenseSeats WHERE AssetId = @Id AND TenantId = @TenantId;
        DELETE dbo.StockAssignments WHERE AssetId = @Id AND TenantId = @TenantId;
        DELETE dbo.Maintenances WHERE AssetId = @Id AND TenantId = @TenantId;
        DELETE dbo.AssetRequests WHERE AssetId = @Id AND TenantId = @TenantId;
        DELETE dbo.Assets WHERE Id = @Id AND TenantId = @TenantId;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @Id, @item, @loc, 'Deleted';
    COMMIT;
END
GO

-- internal: issues an asset to a person (used by check-out and by request approval)
CREATE OR ALTER PROCEDURE dbo.PRO_AMS_ASSET_ISSUE
    @TenantId UNIQUEIDENTIFIER, @Id INT, @EmpCode NVARCHAR(30), @EmpName NVARCHAR(200), @EmpEmail NVARCHAR(200) = NULL,
    @ExpectedCheckin DATE = NULL, @Note NVARCHAR(1000) = NULL, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @tag NVARCHAR(40), @name NVARCHAR(160), @loc NVARCHAR(30), @holder NVARCHAR(200), @statusType NVARCHAR(20), @status NVARCHAR(80);
    SELECT @tag = AssetTag, @name = Name, @loc = LocationCode, @holder = AssignedToName, @statusType = StatusType, @status = StatusName
    FROM dbo.vw_Assets WITH (UPDLOCK) WHERE Id = @Id AND TenantId = @TenantId;
    IF @tag IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    IF @holder IS NOT NULL BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@tag, ' is already checked out to ', @holder, '.'); RETURN; END
    IF @statusType <> 'Deployable' BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@tag, ' is ''', @status, '''; only deployable assets can be checked out.'); RETURN; END
    UPDATE dbo.Assets SET AssignedToEmpCode = @EmpCode, AssignedToName = @EmpName, AssignedToEmail = @EmpEmail, AssignedAt = @Now,
           ExpectedCheckin = @ExpectedCheckin, UpdatedAt = @Now
    WHERE Id = @Id AND TenantId = @TenantId;
    DECLARE @item NVARCHAR(300) = CONCAT(@tag, ' ', @name), @target NVARCHAR(300) = CONCAT(@EmpName, ' (', @EmpCode, ')');
    EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @Id, @item, @loc, 'CheckedOut', @target, @Note;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_ASSET_CHECKOUT
    @TenantId UNIQUEIDENTIFIER, @Id INT, @EmpCode NVARCHAR(30), @EmpName NVARCHAR(200), @EmpEmail NVARCHAR(200) = NULL,
    @ExpectedCheckin DATE = NULL, @Note NVARCHAR(1000) = NULL, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRANSACTION;
        EXEC dbo.PRO_AMS_ASSET_ISSUE @TenantId, @Id, @EmpCode, @EmpName, @EmpEmail, @ExpectedCheckin, @Note, @Now, @ActorEmpCode, @ActorName,
             @Error_Code OUTPUT, @Error_Message OUTPUT;
    IF @Error_Code = 0 COMMIT; ELSE ROLLBACK;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_ASSET_CHECKIN
    @TenantId UNIQUEIDENTIFIER, @Id INT, @StatusLabelId INT = NULL, @Note NVARCHAR(1000) = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @tag NVARCHAR(40), @name NVARCHAR(160), @loc NVARCHAR(30), @code NVARCHAR(30), @holder NVARCHAR(200);
    SELECT @tag = AssetTag, @name = Name, @loc = LocationCode, @code = AssignedToEmpCode, @holder = AssignedToName FROM dbo.vw_Assets WHERE Id = @Id AND TenantId = @TenantId;
    IF @tag IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    IF @code IS NULL BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@tag, ' is not checked out.'); RETURN; END
    IF @StatusLabelId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.StatusLabels WHERE Id = @StatusLabelId AND TenantId = @TenantId)
    BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Unknown status.'; RETURN; END
    BEGIN TRANSACTION;
        UPDATE dbo.Assets SET AssignedToEmpCode = NULL, AssignedToName = NULL, AssignedToEmail = NULL, AssignedAt = NULL, ExpectedCheckin = NULL,
               StatusLabelId = ISNULL(@StatusLabelId, StatusLabelId), UpdatedAt = @Now
        WHERE Id = @Id AND TenantId = @TenantId;
        DECLARE @item NVARCHAR(300) = CONCAT(@tag, ' ', @name), @target NVARCHAR(300) = CONCAT(@holder, ' (', @code, ')');
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @Id, @item, @loc, 'CheckedIn', @target, @Note;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_ASSET_AUDIT
    @TenantId UNIQUEIDENTIFIER, @Id INT, @NextAuditDate DATE = NULL, @Note NVARCHAR(1000) = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @item NVARCHAR(300), @loc NVARCHAR(30), @today DATE = CAST(@Now AS DATE);
    SELECT @item = CONCAT(AssetTag, ' ', Name), @loc = LocationCode FROM dbo.vw_Assets WHERE Id = @Id AND TenantId = @TenantId;
    IF @item IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    IF @NextAuditDate IS NOT NULL AND @NextAuditDate <= @today BEGIN SELECT @Error_Code = 1002, @Error_Message = 'The next audit must be in the future.'; RETURN; END
    BEGIN TRANSACTION;
        UPDATE dbo.Assets SET LastAuditDate = @today, NextAuditDate = ISNULL(@NextAuditDate, DATEADD(MONTH, 12, @today)), UpdatedAt = @Now
        WHERE Id = @Id AND TenantId = @TenantId;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @Id, @item, @loc, 'Audited', NULL, @Note;
    COMMIT;
END
GO

-- everything issued to one person (4 result sets: assets, licence seats, accessories, consumables)
CREATE OR ALTER PROCEDURE dbo.PRO_AMS_MY_ITEMS
    @TenantId UNIQUEIDENTIFIER, @EmpCode NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.vw_Assets WHERE TenantId = @TenantId AND AssignedToEmpCode = @EmpCode ORDER BY AssetTag;
    SELECT s.Id, s.LicenseId, l.Name AS LicenseName, s.AssignedToEmpCode, s.AssignedToName, s.AssetId, a.AssetTag, s.AssignedAt
    FROM dbo.LicenseSeats s JOIN dbo.Licenses l ON l.Id = s.LicenseId LEFT JOIN dbo.Assets a ON a.Id = s.AssetId
    WHERE s.TenantId = @TenantId AND s.AssignedToEmpCode = @EmpCode ORDER BY s.AssignedAt DESC;
    SELECT x.Id, x.StockItemId, i.Name AS ItemName, i.Kind, x.EmpCode, x.EmpName, x.AssetId, x.Quantity, x.AssignedAt
    FROM dbo.StockAssignments x JOIN dbo.StockItems i ON i.Id = x.StockItemId
    WHERE x.TenantId = @TenantId AND x.EmpCode = @EmpCode AND i.Kind = 'Accessory' ORDER BY x.AssignedAt DESC;
    SELECT TOP (20) x.Id, x.StockItemId, i.Name AS ItemName, i.Kind, x.EmpCode, x.EmpName, x.AssetId, x.Quantity, x.AssignedAt
    FROM dbo.StockAssignments x JOIN dbo.StockItems i ON i.Id = x.StockItemId
    WHERE x.TenantId = @TenantId AND x.EmpCode = @EmpCode AND i.Kind = 'Consumable' ORDER BY x.AssignedAt DESC;
END
GO

-- ============================================================== maintenance

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_MAINTENANCE_LIST
    @TenantId UNIQUEIDENTIFIER, @OpenOnly BIT = 0, @AssetId INT = NULL, @Id INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT x.Id, x.AssetId, x.MaintenanceType, x.Title, x.SupplierId, sp.Name AS SupplierName, x.StartDate, x.CompletionDate, x.Cost, x.IsWarranty, x.Notes,
           a.AssetTag, a.Name AS AssetName, a.PurchaseCost AS AssetCost, a.LocationCode, a.Region, a.CategoryName, a.StatusName, a.StatusType, a.DepartmentName,
           a.AssignedToEmpCode
    FROM dbo.Maintenances x JOIN dbo.vw_Assets a ON a.Id = x.AssetId LEFT JOIN dbo.Suppliers sp ON sp.Id = x.SupplierId
    WHERE x.TenantId = @TenantId AND (@OpenOnly = 0 OR x.CompletionDate IS NULL) AND (@AssetId IS NULL OR x.AssetId = @AssetId) AND (@Id IS NULL OR x.Id = @Id)
    ORDER BY x.StartDate DESC, x.Id DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_MAINTENANCE_SAVE
    @TenantId UNIQUEIDENTIFIER, @Id INT = 0, @AssetId INT, @MaintenanceType NVARCHAR(20), @Title NVARCHAR(200), @SupplierId INT = NULL,
    @StartDate DATE, @CompletionDate DATE = NULL, @Cost DECIMAL(18,2) = 0, @IsWarranty BIT = 0, @Notes NVARCHAR(1000) = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS', @Record_Id = NULLIF(@Id, 0), @Title = LTRIM(RTRIM(@Title));
    IF ISNULL(@Title, '') = '' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Title is required.'; RETURN; END
    IF @Cost < 0 BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Cost cannot be negative.'; RETURN; END
    IF @CompletionDate IS NOT NULL AND @CompletionDate < @StartDate BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Completion date cannot be before the start date.'; RETURN; END
    DECLARE @item NVARCHAR(300), @loc NVARCHAR(30);
    SELECT @item = CONCAT(AssetTag, ' ', Name), @loc = LocationCode FROM dbo.vw_Assets WHERE Id = @AssetId AND TenantId = @TenantId;
    IF @item IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    BEGIN TRANSACTION;
        IF @Record_Id IS NULL
        BEGIN
            INSERT INTO dbo.Maintenances (TenantId, AssetId, MaintenanceType, Title, SupplierId, StartDate, CompletionDate, Cost, IsWarranty, Notes)
            VALUES (@TenantId, @AssetId, @MaintenanceType, @Title, @SupplierId, @StartDate, @CompletionDate, @Cost, @IsWarranty, @Notes);
            SET @Record_Id = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.Maintenances SET MaintenanceType = @MaintenanceType, Title = @Title, SupplierId = @SupplierId, StartDate = @StartDate,
                   CompletionDate = @CompletionDate, Cost = @Cost, IsWarranty = @IsWarranty, Notes = @Notes
            WHERE Id = @Record_Id AND TenantId = @TenantId AND AssetId = @AssetId;
            IF @@ROWCOUNT = 0 BEGIN ROLLBACK; SELECT @Error_Code = 1001, @Error_Message = 'Maintenance record not found.'; RETURN; END
        END
        DECLARE @note NVARCHAR(1000) = CASE WHEN @CompletionDate IS NOT NULL THEN CONCAT('Completed: ', @Title) WHEN @Id > 0 THEN CONCAT('Updated: ', @Title) ELSE @Title END;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @AssetId, @item, @loc, 'Maintenance', @MaintenanceType, @note;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_MAINTENANCE_DELETE
    @TenantId UNIQUEIDENTIFIER, @Id INT, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DELETE dbo.Maintenances WHERE Id = @Id AND TenantId = @TenantId;
    IF @@ROWCOUNT = 0 SELECT @Error_Code = 1001, @Error_Message = 'Maintenance record not found.';
END
GO

-- ============================================================== asset requests

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_REQUEST_LIST
    @TenantId UNIQUEIDENTIFIER, @Status NVARCHAR(20) = NULL, @Id INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.Id, r.AssetId, r.RequestedByEmpCode, r.RequestedByName, r.RequestedByEmail, r.Reason, r.Status, r.RequestedAt, r.DecidedByEmpCode, r.DecidedByName,
           r.DecidedAt, r.DecisionNote, a.AssetTag, a.Name AS AssetName, a.PurchaseCost AS AssetCost, a.LocationCode, a.Region, a.CategoryName, a.StatusName,
           a.StatusType, a.DepartmentName, a.Requestable
    FROM dbo.AssetRequests r JOIN dbo.vw_Assets a ON a.Id = r.AssetId
    WHERE r.TenantId = @TenantId AND (@Status IS NULL OR r.Status = @Status) AND (@Id IS NULL OR r.Id = @Id)
    ORDER BY r.RequestedAt DESC, r.Id DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_REQUEST_CREATE
    @TenantId UNIQUEIDENTIFIER, @AssetId INT, @Reason NVARCHAR(1000), @EmpCode NVARCHAR(30), @EmpName NVARCHAR(200), @EmpEmail NVARCHAR(200) = NULL,
    @Now DATETIME2, @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS', @Reason = LTRIM(RTRIM(@Reason));
    IF ISNULL(@Reason, '') = '' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Please give a reason.'; RETURN; END
    DECLARE @tag NVARCHAR(40), @name NVARCHAR(160), @loc NVARCHAR(30), @requestable BIT, @holder NVARCHAR(30), @statusType NVARCHAR(20);
    SELECT @tag = AssetTag, @name = Name, @loc = LocationCode, @requestable = Requestable, @holder = AssignedToEmpCode, @statusType = StatusType
    FROM dbo.vw_Assets WHERE Id = @AssetId AND TenantId = @TenantId;
    IF @tag IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    IF @requestable = 0 OR @statusType <> 'Deployable' BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@tag, ' is not offered for requests.'); RETURN; END
    IF @holder IS NOT NULL BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@tag, ' is already issued.'); RETURN; END
    IF EXISTS (SELECT 1 FROM dbo.AssetRequests WHERE TenantId = @TenantId AND AssetId = @AssetId AND RequestedByEmpCode = @EmpCode AND Status = 'Pending')
    BEGIN SELECT @Error_Code = 1003, @Error_Message = 'You already have a pending request for this asset.'; RETURN; END
    BEGIN TRANSACTION;
        INSERT INTO dbo.AssetRequests (TenantId, AssetId, RequestedByEmpCode, RequestedByName, RequestedByEmail, Reason, Status, RequestedAt)
        VALUES (@TenantId, @AssetId, @EmpCode, @EmpName, @EmpEmail, @Reason, 'Pending', @Now);
        SET @Record_Id = SCOPE_IDENTITY();
        DECLARE @item NVARCHAR(300) = CONCAT(@tag, ' ', @name), @target NVARCHAR(300) = CONCAT(@EmpName, ' (', @EmpCode, ')');
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @EmpCode, @EmpName, 'Request', @Record_Id, @item, @loc, 'Requested', @target, @Reason;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @EmpCode, @EmpName, 'Asset', @AssetId, @item, @loc, 'Requested', @target, @Reason;
    COMMIT;
END
GO

-- approve (issues the asset to the requester and rejects other pending requests for it) or reject
CREATE OR ALTER PROCEDURE dbo.PRO_AMS_REQUEST_DECIDE
    @TenantId UNIQUEIDENTIFIER, @Id INT, @Approve BIT, @Note NVARCHAR(1000) = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS', @Note = NULLIF(LTRIM(RTRIM(@Note)), '');
    DECLARE @assetId INT, @status NVARCHAR(20), @code NVARCHAR(30), @name NVARCHAR(200), @email NVARCHAR(200);
    SELECT @assetId = AssetId, @status = Status, @code = RequestedByEmpCode, @name = RequestedByName, @email = RequestedByEmail
    FROM dbo.AssetRequests WITH (UPDLOCK) WHERE Id = @Id AND TenantId = @TenantId;
    IF @assetId IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Request not found.'; RETURN; END
    IF @status <> 'Pending' BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT('The request is already ', LOWER(@status), '.'); RETURN; END
    IF @Approve = 0 AND @Note IS NULL BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Give a reason for the rejection.'; RETURN; END

    DECLARE @tag NVARCHAR(40), @asset NVARCHAR(160), @loc NVARCHAR(30);
    SELECT @tag = AssetTag, @asset = Name, @loc = LocationCode FROM dbo.vw_Assets WHERE Id = @assetId;
    DECLARE @item NVARCHAR(300) = CONCAT(@tag, ' ', @asset), @target NVARCHAR(300) = CONCAT(@name, ' (', @code, ')');

    BEGIN TRANSACTION;
    IF @Approve = 1
    BEGIN
        DECLARE @issueNote NVARCHAR(1000) = CONCAT('Request approved', CASE WHEN @Note IS NULL THEN '' ELSE ': ' + @Note END);
        EXEC dbo.PRO_AMS_ASSET_ISSUE @TenantId, @assetId, @code, @name, @email, NULL, @issueNote, @Now, @ActorEmpCode, @ActorName,
             @Error_Code OUTPUT, @Error_Message OUTPUT;
        IF @Error_Code <> 0 BEGIN ROLLBACK; RETURN; END
        UPDATE dbo.AssetRequests SET Status = 'Approved', DecidedByEmpCode = @ActorEmpCode, DecidedByName = @ActorName, DecidedAt = @Now, DecisionNote = @Note WHERE Id = @Id;
        UPDATE dbo.AssetRequests SET Status = 'Rejected', DecidedByEmpCode = @ActorEmpCode, DecidedByName = @ActorName, DecidedAt = @Now,
               DecisionNote = CONCAT('Asset issued to ', @name)
        WHERE TenantId = @TenantId AND AssetId = @assetId AND Status = 'Pending' AND Id <> @Id;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Request', @Id, @item, @loc, 'Approved', @target, @Note;
    END
    ELSE
    BEGIN
        UPDATE dbo.AssetRequests SET Status = 'Rejected', DecidedByEmpCode = @ActorEmpCode, DecidedByName = @ActorName, DecidedAt = @Now, DecisionNote = @Note WHERE Id = @Id;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Request', @Id, @item, @loc, 'Rejected', @target, @Note;
    END
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_REQUEST_CANCEL
    @TenantId UNIQUEIDENTIFIER, @Id INT, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @status NVARCHAR(20), @item NVARCHAR(300), @loc NVARCHAR(30), @target NVARCHAR(300);
    SELECT @status = r.Status, @item = CONCAT(a.AssetTag, ' ', a.Name), @loc = a.LocationCode, @target = CONCAT(r.RequestedByName, ' (', r.RequestedByEmpCode, ')')
    FROM dbo.AssetRequests r JOIN dbo.vw_Assets a ON a.Id = r.AssetId WHERE r.Id = @Id AND r.TenantId = @TenantId;
    IF @status IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Request not found.'; RETURN; END
    IF @status <> 'Pending' BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT('The request is already ', LOWER(@status), '.'); RETURN; END
    BEGIN TRANSACTION;
        UPDATE dbo.AssetRequests SET Status = 'Cancelled', DecidedByEmpCode = @ActorEmpCode, DecidedByName = @ActorName, DecidedAt = @Now WHERE Id = @Id;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Request', @Id, @item, @loc, 'Cancelled', @target;
    COMMIT;
END
GO

-- ============================================================== licences

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_LICENSE_LIST
    @TenantId UNIQUEIDENTIFIER, @Search NVARCHAR(100) = NULL, @Id INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT l.Id, l.Name, l.ProductKey, l.Seats, l.ManufacturerId, f.Name AS ManufacturerName, l.CategoryId, c.Name AS CategoryName, l.SupplierId,
           s.Name AS SupplierName, l.PurchaseDate, l.PurchaseCost, l.ExpirationDate, l.Notes,
           (SELECT COUNT(*) FROM dbo.LicenseSeats x WHERE x.LicenseId = l.Id) AS Used
    FROM dbo.Licenses l LEFT JOIN dbo.Manufacturers f ON f.Id = l.ManufacturerId LEFT JOIN dbo.Categories c ON c.Id = l.CategoryId
    LEFT JOIN dbo.Suppliers s ON s.Id = l.SupplierId
    WHERE l.TenantId = @TenantId AND (@Id IS NULL OR l.Id = @Id)
      AND (@Search IS NULL OR l.Name LIKE '%' + @Search + '%' OR f.Name LIKE '%' + @Search + '%')
    ORDER BY l.Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_LICENSE_SAVE
    @TenantId UNIQUEIDENTIFIER, @Id INT = 0, @Name NVARCHAR(200), @ProductKey NVARCHAR(500) = NULL, @Seats INT,
    @ManufacturerId INT = NULL, @CategoryId INT = NULL, @SupplierId INT = NULL, @PurchaseDate DATE = NULL, @PurchaseCost DECIMAL(18,2) = 0,
    @ExpirationDate DATE = NULL, @Notes NVARCHAR(1000) = NULL, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS', @Record_Id = NULLIF(@Id, 0), @Name = LTRIM(RTRIM(@Name));
    IF ISNULL(@Name, '') = '' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Name is required.'; RETURN; END
    IF @Seats < 1 BEGIN SELECT @Error_Code = 1002, @Error_Message = 'A licence needs at least one seat.'; RETURN; END
    IF @PurchaseCost < 0 BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Cost cannot be negative.'; RETURN; END
    DECLARE @used INT = (SELECT COUNT(*) FROM dbo.LicenseSeats WHERE LicenseId = ISNULL(@Record_Id, 0) AND TenantId = @TenantId);
    IF @Seats < @used BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@used, ' seats are in use; check some in before reducing seats to ', @Seats, '.'); RETURN; END
    BEGIN TRANSACTION;
        IF @Record_Id IS NULL
        BEGIN
            INSERT INTO dbo.Licenses (TenantId, Name, ProductKey, Seats, ManufacturerId, CategoryId, SupplierId, PurchaseDate, PurchaseCost, ExpirationDate, Notes)
            VALUES (@TenantId, @Name, NULLIF(LTRIM(RTRIM(@ProductKey)), ''), @Seats, @ManufacturerId, @CategoryId, @SupplierId, @PurchaseDate, @PurchaseCost, @ExpirationDate, @Notes);
            SET @Record_Id = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.Licenses SET Name = @Name, ProductKey = NULLIF(LTRIM(RTRIM(@ProductKey)), ''), Seats = @Seats, ManufacturerId = @ManufacturerId,
                   CategoryId = @CategoryId, SupplierId = @SupplierId, PurchaseDate = @PurchaseDate, PurchaseCost = @PurchaseCost, ExpirationDate = @ExpirationDate, Notes = @Notes
            WHERE Id = @Record_Id AND TenantId = @TenantId;
            IF @@ROWCOUNT = 0 BEGIN ROLLBACK; SELECT @Error_Code = 1001, @Error_Message = 'Licence not found.'; RETURN; END
        END
        DECLARE @act NVARCHAR(20) = CASE WHEN @Id > 0 THEN 'Updated' ELSE 'Created' END, @target NVARCHAR(300) = CONCAT(@Seats, ' seats');
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'License', @Record_Id, @Name, NULL, @act, @target;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_LICENSE_DELETE
    @TenantId UNIQUEIDENTIFIER, @Id INT, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @name NVARCHAR(200) = (SELECT Name FROM dbo.Licenses WHERE Id = @Id AND TenantId = @TenantId);
    IF @name IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Licence not found.'; RETURN; END
    IF EXISTS (SELECT 1 FROM dbo.LicenseSeats WHERE LicenseId = @Id) BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Check in all seats before deleting the licence.'; RETURN; END
    BEGIN TRANSACTION;
        DELETE dbo.Licenses WHERE Id = @Id AND TenantId = @TenantId;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'License', @Id, @name, NULL, 'Deleted';
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_LICENSE_SEATS
    @TenantId UNIQUEIDENTIFIER, @LicenseId INT = NULL, @SeatId INT = NULL, @AssetId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.Id, s.LicenseId, l.Name AS LicenseName, s.AssignedToEmpCode, s.AssignedToName, s.AssetId, a.AssetTag, a.Name AS AssetName, a.LocationCode, s.AssignedAt
    FROM dbo.LicenseSeats s JOIN dbo.Licenses l ON l.Id = s.LicenseId LEFT JOIN dbo.vw_Assets a ON a.Id = s.AssetId
    WHERE s.TenantId = @TenantId AND (@LicenseId IS NULL OR s.LicenseId = @LicenseId) AND (@SeatId IS NULL OR s.Id = @SeatId)
      AND (@AssetId IS NULL OR s.AssetId = @AssetId)
    ORDER BY s.AssignedAt;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_LICENSE_CHECKOUT
    @TenantId UNIQUEIDENTIFIER, @LicenseId INT, @EmpCode NVARCHAR(30) = NULL, @EmpName NVARCHAR(200) = NULL, @AssetId INT = NULL, @Note NVARCHAR(1000) = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    IF @EmpCode IS NULL AND @AssetId IS NULL BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Choose a person or an asset.'; RETURN; END
    BEGIN TRANSACTION;
    DECLARE @name NVARCHAR(200), @seats INT, @expires DATE;
    SELECT @name = Name, @seats = Seats, @expires = ExpirationDate FROM dbo.Licenses WITH (UPDLOCK) WHERE Id = @LicenseId AND TenantId = @TenantId;
    IF @name IS NULL BEGIN ROLLBACK; SELECT @Error_Code = 1001, @Error_Message = 'Licence not found.'; RETURN; END
    IF (SELECT COUNT(*) FROM dbo.LicenseSeats WHERE LicenseId = @LicenseId) >= @seats
    BEGIN ROLLBACK; SELECT @Error_Code = 1002, @Error_Message = CONCAT('All ', @seats, ' seats of ', @name, ' are in use.'); RETURN; END
    IF @expires IS NOT NULL AND @expires < CAST(@Now AS DATE)
    BEGIN ROLLBACK; SELECT @Error_Code = 1002, @Error_Message = CONCAT(@name, ' expired on ', FORMAT(@expires, 'dd MMM yyyy'), '.'); RETURN; END
    IF @EmpCode IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.LicenseSeats WHERE LicenseId = @LicenseId AND AssignedToEmpCode = @EmpCode)
    BEGIN ROLLBACK; SELECT @Error_Code = 1003, @Error_Message = CONCAT(@EmpName, ' already has a seat.'); RETURN; END
    DECLARE @tag NVARCHAR(40), @asset NVARCHAR(160), @loc NVARCHAR(30);
    IF @AssetId IS NOT NULL
    BEGIN
        SELECT @tag = AssetTag, @asset = Name, @loc = LocationCode FROM dbo.vw_Assets WHERE Id = @AssetId AND TenantId = @TenantId;
        IF @tag IS NULL BEGIN ROLLBACK; SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    END
    INSERT INTO dbo.LicenseSeats (TenantId, LicenseId, AssignedToEmpCode, AssignedToName, AssetId, AssignedAt) VALUES (@TenantId, @LicenseId, @EmpCode, @EmpName, @AssetId, @Now);
    SET @Record_Id = SCOPE_IDENTITY();
    DECLARE @target NVARCHAR(300) = CONCAT_WS(' / ', CASE WHEN @EmpCode IS NOT NULL THEN CONCAT(@EmpName, ' (', @EmpCode, ')') END, @tag);
    EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'License', @LicenseId, @name, @loc, 'CheckedOut', @target, @Note;
    IF @AssetId IS NOT NULL
    BEGIN
        DECLARE @assetItem NVARCHAR(300) = CONCAT(@tag, ' ', @asset), @lic NVARCHAR(300) = CONCAT('Licence ', @name);
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @AssetId, @assetItem, @loc, 'CheckedOut', @lic, @Note;
    END
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_LICENSE_CHECKIN
    @TenantId UNIQUEIDENTIFIER, @SeatId INT, @Note NVARCHAR(1000) = NULL, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @licenseId INT, @name NVARCHAR(200), @target NVARCHAR(300), @loc NVARCHAR(30);
    SELECT @licenseId = s.LicenseId, @name = l.Name, @loc = a.LocationCode, @target = CONCAT_WS(' / ', s.AssignedToName, a.AssetTag)
    FROM dbo.LicenseSeats s JOIN dbo.Licenses l ON l.Id = s.LicenseId LEFT JOIN dbo.vw_Assets a ON a.Id = s.AssetId
    WHERE s.Id = @SeatId AND s.TenantId = @TenantId;
    IF @licenseId IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Seat not found.'; RETURN; END
    BEGIN TRANSACTION;
        DELETE dbo.LicenseSeats WHERE Id = @SeatId AND TenantId = @TenantId;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'License', @licenseId, @name, @loc, 'CheckedIn', @target, @Note;
    COMMIT;
END
GO

-- ============================================================== accessories, consumables, components

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_STOCK_LIST
    @TenantId UNIQUEIDENTIFIER, @Kind NVARCHAR(20) = NULL, @Search NVARCHAR(100) = NULL, @Id INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.Id, i.Kind, i.Name, i.CategoryId, c.Name AS CategoryName, i.ManufacturerId, f.Name AS ManufacturerName, i.LocationId, l.Code AS LocationCode,
           l.Name AS LocationName, l.Region, i.ModelNumber, i.Serial, i.Quantity, i.MinQuantity, i.UnitCost, i.Notes,
           CASE WHEN i.Kind = 'Consumable' THEN 0 ELSE ISNULL((SELECT SUM(x.Quantity) FROM dbo.StockAssignments x WHERE x.StockItemId = i.Id), 0) END AS InUse
    FROM dbo.StockItems i JOIN dbo.Locations l ON l.Id = i.LocationId LEFT JOIN dbo.Categories c ON c.Id = i.CategoryId
    LEFT JOIN dbo.Manufacturers f ON f.Id = i.ManufacturerId
    WHERE i.TenantId = @TenantId AND (@Kind IS NULL OR i.Kind = @Kind) AND (@Id IS NULL OR i.Id = @Id)
      AND (@Search IS NULL OR i.Name LIKE '%' + @Search + '%' OR i.ModelNumber LIKE '%' + @Search + '%' OR c.Name LIKE '%' + @Search + '%' OR l.Code LIKE '%' + @Search + '%')
    ORDER BY i.Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_STOCK_SAVE
    @TenantId UNIQUEIDENTIFIER, @Id INT = 0, @Kind NVARCHAR(20), @Name NVARCHAR(200), @CategoryId INT = NULL, @ManufacturerId INT = NULL, @LocationId INT,
    @ModelNumber NVARCHAR(80) = NULL, @Serial NVARCHAR(100) = NULL, @Quantity INT, @MinQuantity INT = 0, @UnitCost DECIMAL(18,2) = 0, @Notes NVARCHAR(1000) = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS', @Record_Id = NULLIF(@Id, 0), @Name = LTRIM(RTRIM(@Name));
    IF ISNULL(@Name, '') = '' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Name is required.'; RETURN; END
    IF @Quantity < 0 OR @MinQuantity < 0 BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Quantities cannot be negative.'; RETURN; END
    IF @UnitCost < 0 BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Cost cannot be negative.'; RETURN; END
    DECLARE @loc NVARCHAR(30) = (SELECT Code FROM dbo.Locations WHERE Id = @LocationId AND TenantId = @TenantId);
    IF @loc IS NULL BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Choose a location.'; RETURN; END
    DECLARE @inUse INT = CASE WHEN @Kind = 'Consumable' THEN 0 ELSE ISNULL((SELECT SUM(Quantity) FROM dbo.StockAssignments WHERE StockItemId = ISNULL(@Record_Id, 0)), 0) END;
    IF @Quantity < @inUse BEGIN SELECT @Error_Code = 1002, @Error_Message = CONCAT(@inUse, ' units are out; quantity cannot be less than that.'); RETURN; END
    BEGIN TRANSACTION;
        IF @Record_Id IS NULL
        BEGIN
            INSERT INTO dbo.StockItems (TenantId, Kind, Name, CategoryId, ManufacturerId, LocationId, ModelNumber, Serial, Quantity, MinQuantity, UnitCost, Notes)
            VALUES (@TenantId, @Kind, @Name, @CategoryId, @ManufacturerId, @LocationId, NULLIF(@ModelNumber, ''), NULLIF(@Serial, ''), @Quantity, @MinQuantity, @UnitCost, @Notes);
            SET @Record_Id = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.StockItems SET Name = @Name, CategoryId = @CategoryId, ManufacturerId = @ManufacturerId, LocationId = @LocationId, ModelNumber = NULLIF(@ModelNumber, ''),
                   Serial = NULLIF(@Serial, ''), Quantity = @Quantity, MinQuantity = @MinQuantity, UnitCost = @UnitCost, Notes = @Notes
            WHERE Id = @Record_Id AND TenantId = @TenantId AND Kind = @Kind;
            IF @@ROWCOUNT = 0 BEGIN ROLLBACK; SELECT @Error_Code = 1001, @Error_Message = CONCAT(@Kind, ' not found.'); RETURN; END
        END
        DECLARE @act NVARCHAR(20) = CASE WHEN @Id > 0 THEN 'Updated' ELSE 'Created' END, @target NVARCHAR(300) = CONCAT('Qty ', @Quantity);
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, @Kind, @Record_Id, @Name, @loc, @act, @target;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_STOCK_DELETE
    @TenantId UNIQUEIDENTIFIER, @Id INT, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @kind NVARCHAR(20), @name NVARCHAR(200), @loc NVARCHAR(30);
    SELECT @kind = i.Kind, @name = i.Name, @loc = l.Code FROM dbo.StockItems i JOIN dbo.Locations l ON l.Id = i.LocationId WHERE i.Id = @Id AND i.TenantId = @TenantId;
    IF @kind IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Item not found.'; RETURN; END
    IF @kind <> 'Consumable' AND EXISTS (SELECT 1 FROM dbo.StockAssignments WHERE StockItemId = @Id)
    BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Check in all units before deleting.'; RETURN; END
    BEGIN TRANSACTION;
        DELETE dbo.StockItems WHERE Id = @Id AND TenantId = @TenantId;   -- consumable issue history goes with it (cascade)
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, @kind, @Id, @name, @loc, 'Deleted';
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.PRO_AMS_STOCK_HOLDERS
    @TenantId UNIQUEIDENTIFIER, @StockItemId INT = NULL, @AssetId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (500) x.Id, x.StockItemId, i.Name AS ItemName, i.Kind, x.EmpCode, x.EmpName, x.AssetId, a.AssetTag, a.Name AS AssetName, x.Quantity, x.AssignedAt
    FROM dbo.StockAssignments x JOIN dbo.StockItems i ON i.Id = x.StockItemId LEFT JOIN dbo.Assets a ON a.Id = x.AssetId
    WHERE x.TenantId = @TenantId AND (@StockItemId IS NULL OR x.StockItemId = @StockItemId) AND (@AssetId IS NULL OR x.AssetId = @AssetId)
    ORDER BY x.AssignedAt DESC;
END
GO

-- accessory → lend one to a person · consumable → issue a quantity (used up) · component → install a quantity into an asset
CREATE OR ALTER PROCEDURE dbo.PRO_AMS_STOCK_CHECKOUT
    @TenantId UNIQUEIDENTIFIER, @StockItemId INT, @EmpCode NVARCHAR(30) = NULL, @EmpName NVARCHAR(200) = NULL, @AssetId INT = NULL,
    @Quantity INT = 1, @Note NVARCHAR(1000) = NULL, @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Record_Id INT = NULL OUTPUT, @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    BEGIN TRANSACTION;
    DECLARE @kind NVARCHAR(20), @name NVARCHAR(200), @qty INT, @loc NVARCHAR(30);
    SELECT @kind = i.Kind, @name = i.Name, @qty = i.Quantity, @loc = l.Code
    FROM dbo.StockItems i WITH (UPDLOCK) JOIN dbo.Locations l ON l.Id = i.LocationId WHERE i.Id = @StockItemId AND i.TenantId = @TenantId;
    IF @kind IS NULL BEGIN ROLLBACK; SELECT @Error_Code = 1001, @Error_Message = 'Item not found.'; RETURN; END
    IF @kind = 'Accessory' SET @Quantity = 1;
    IF ISNULL(@Quantity, 0) < 1 BEGIN ROLLBACK; SELECT @Error_Code = 1002, @Error_Message = 'Quantity must be at least 1.'; RETURN; END
    IF @kind = 'Component' AND @AssetId IS NULL BEGIN ROLLBACK; SELECT @Error_Code = 1002, @Error_Message = 'Choose the asset the component goes into.'; RETURN; END
    IF @kind <> 'Component' AND @EmpCode IS NULL BEGIN ROLLBACK; SELECT @Error_Code = 1002, @Error_Message = 'Choose a person.'; RETURN; END
    DECLARE @available INT = @qty - CASE WHEN @kind = 'Consumable' THEN 0 ELSE ISNULL((SELECT SUM(Quantity) FROM dbo.StockAssignments WHERE StockItemId = @StockItemId), 0) END;
    IF @Quantity > @available BEGIN ROLLBACK; SELECT @Error_Code = 1002, @Error_Message = CONCAT('Only ', @available, ' of ', @name, ' available.'); RETURN; END
    IF @kind = 'Accessory' AND EXISTS (SELECT 1 FROM dbo.StockAssignments WHERE StockItemId = @StockItemId AND EmpCode = @EmpCode)
    BEGIN ROLLBACK; SELECT @Error_Code = 1003, @Error_Message = CONCAT(@EmpName, ' already has one.'); RETURN; END
    DECLARE @tag NVARCHAR(40), @asset NVARCHAR(160), @assetLoc NVARCHAR(30);
    IF @AssetId IS NOT NULL
    BEGIN
        SELECT @tag = AssetTag, @asset = Name, @assetLoc = LocationCode FROM dbo.vw_Assets WHERE Id = @AssetId AND TenantId = @TenantId;
        IF @tag IS NULL BEGIN ROLLBACK; SELECT @Error_Code = 1001, @Error_Message = 'Asset not found.'; RETURN; END
    END

    INSERT INTO dbo.StockAssignments (TenantId, StockItemId, EmpCode, EmpName, AssetId, Quantity, AssignedAt)
    VALUES (@TenantId, @StockItemId, CASE WHEN @kind = 'Component' THEN NULL ELSE @EmpCode END, CASE WHEN @kind = 'Component' THEN NULL ELSE @EmpName END,
            CASE WHEN @kind = 'Component' THEN @AssetId END, @Quantity, @Now);
    SET @Record_Id = SCOPE_IDENTITY();
    IF @kind = 'Consumable' UPDATE dbo.StockItems SET Quantity = Quantity - @Quantity WHERE Id = @StockItemId;

    DECLARE @target NVARCHAR(300) = CASE @kind WHEN 'Accessory' THEN CONCAT(@EmpName, ' (', @EmpCode, ')')
                                               WHEN 'Consumable' THEN CONCAT(@Quantity, ' x to ', @EmpName, ' (', @EmpCode, ')')
                                               ELSE CONCAT(@Quantity, ' x into ', @tag) END;
    EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, @kind, @StockItemId, @name, @loc, 'CheckedOut', @target, @Note;
    IF @kind = 'Component'
    BEGIN
        DECLARE @assetItem NVARCHAR(300) = CONCAT(@tag, ' ', @asset), @installed NVARCHAR(300) = CONCAT('Installed ', @Quantity, ' x ', @name);
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, 'Asset', @AssetId, @assetItem, @assetLoc, 'Updated', @installed, @Note;
    END
    COMMIT;
END
GO

-- returns a lent accessory or removes an installed component (consumables are used up and cannot be checked in)
CREATE OR ALTER PROCEDURE dbo.PRO_AMS_STOCK_CHECKIN
    @TenantId UNIQUEIDENTIFIER, @StockItemId INT, @AssignmentId INT, @Note NVARCHAR(1000) = NULL,
    @Now DATETIME2, @ActorEmpCode NVARCHAR(30), @ActorName NVARCHAR(200),
    @Error_Code INT OUTPUT, @Error_Message NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = 'SUCCESS';
    DECLARE @kind NVARCHAR(20), @name NVARCHAR(200), @loc NVARCHAR(30), @target NVARCHAR(300);
    SELECT @kind = i.Kind, @name = i.Name, @loc = l.Code,
           @target = CASE WHEN x.AssetId IS NULL THEN CONCAT(x.EmpName, ' (', x.EmpCode, ')') ELSE CONCAT(x.Quantity, ' x from ', a.AssetTag) END
    FROM dbo.StockAssignments x JOIN dbo.StockItems i ON i.Id = x.StockItemId JOIN dbo.Locations l ON l.Id = i.LocationId LEFT JOIN dbo.Assets a ON a.Id = x.AssetId
    WHERE x.Id = @AssignmentId AND x.StockItemId = @StockItemId AND x.TenantId = @TenantId;
    IF @kind IS NULL BEGIN SELECT @Error_Code = 1001, @Error_Message = 'Checkout not found.'; RETURN; END
    IF @kind = 'Consumable' BEGIN SELECT @Error_Code = 1002, @Error_Message = 'Consumables are used up and cannot be checked in.'; RETURN; END
    BEGIN TRANSACTION;
        DELETE dbo.StockAssignments WHERE Id = @AssignmentId AND TenantId = @TenantId;
        EXEC dbo.PRO_AMS_HISTORY_ADD @TenantId, @Now, @ActorEmpCode, @ActorName, @kind, @StockItemId, @name, @loc, 'CheckedIn', @target, @Note;
    COMMIT;
END
GO

-- tenants that have AMS data (used by the daily digest job)
CREATE OR ALTER PROCEDURE dbo.PRO_AMS_TENANTS
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT TenantId FROM dbo.Locations;
END
GO
