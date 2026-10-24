/*
  VMM User Management System - access control schema (SQL Server 2019+)
  Run once against an empty database, e.g.:
      sqlcmd -S localhost -d UMS -i 001_create_access_control_schema.sql
  Every table except Applications/Modules/Resources carries TenantId for isolation.
  Enum columns store the enum name (e.g. 'Restrict', 'Active') for readability.
*/
SET NOCOUNT ON;

CREATE TABLE dbo.Tenants (
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
    Code        NVARCHAR(50)     NOT NULL CONSTRAINT UQ_Tenants_Code UNIQUE,
    Name        NVARCHAR(200)    NOT NULL,
    IsActive    BIT              NOT NULL,
    CreatedOn   DATE             NOT NULL
);

-- password, lockout, MFA and token policy per tenant (SecuritySettings record as JSON)
CREATE TABLE dbo.TenantSettings (
    TenantId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantSettings PRIMARY KEY
                 CONSTRAINT FK_TenantSettings_Tenants REFERENCES dbo.Tenants(Id),
    SettingsJson NVARCHAR(MAX)    NOT NULL
);

CREATE TABLE dbo.Applications (
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Applications PRIMARY KEY,
    Code        NVARCHAR(30)     NOT NULL CONSTRAINT UQ_Applications_Code UNIQUE,
    Name        NVARCHAR(200)    NOT NULL,
    Type        NVARCHAR(20)     NOT NULL,          -- InHouse | ThirdParty
    Vendor      NVARCHAR(200)    NOT NULL,
    OwnerDept   NVARCHAR(200)    NOT NULL,
    Description NVARCHAR(1000)   NOT NULL,
    IsActive    BIT              NOT NULL
);

CREATE TABLE dbo.ApplicationTenants (
    ApplicationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ApplicationTenants_Applications REFERENCES dbo.Applications(Id) ON DELETE CASCADE,
    TenantId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ApplicationTenants_Tenants REFERENCES dbo.Tenants(Id),
    CONSTRAINT PK_ApplicationTenants PRIMARY KEY (ApplicationId, TenantId)
);

CREATE TABLE dbo.Modules (
    Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Modules PRIMARY KEY,
    ApplicationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Modules_Applications REFERENCES dbo.Applications(Id) ON DELETE CASCADE,
    Code          NVARCHAR(50)     NOT NULL,
    Name          NVARCHAR(200)    NOT NULL,
    SortOrder     INT              NOT NULL,
    CONSTRAINT UQ_Modules_Code UNIQUE (ApplicationId, Code)
);

CREATE TABLE dbo.Resources (
    Id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Resources PRIMARY KEY,
    ModuleId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Resources_Modules REFERENCES dbo.Modules(Id) ON DELETE CASCADE,
    Code      NVARCHAR(50)     NOT NULL,
    Name      NVARCHAR(200)    NOT NULL,
    Actions   NVARCHAR(200)    NOT NULL,          -- comma separated: View,Create,Edit,...
    SortOrder INT              NOT NULL,
    CONSTRAINT UQ_Resources_Code UNIQUE (ModuleId, Code)
);

CREATE TABLE dbo.Roles (
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Roles_Tenants REFERENCES dbo.Tenants(Id),
    Code        NVARCHAR(50)     NOT NULL,
    Name        NVARCHAR(200)    NOT NULL,
    Description NVARCHAR(1000)   NOT NULL,
    AppId       UNIQUEIDENTIFIER NULL CONSTRAINT FK_Roles_Applications REFERENCES dbo.Applications(Id) ON DELETE SET NULL,
    IsSystem    BIT              NOT NULL,
    IsActive    BIT              NOT NULL,
    CONSTRAINT UQ_Roles_Code UNIQUE (TenantId, Code)
);

-- APP.MODULE.RESOURCE.ACTION, '*' allowed in any segment
CREATE TABLE dbo.RolePermissions (
    RoleId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_RolePermissions_Roles REFERENCES dbo.Roles(Id) ON DELETE CASCADE,
    PermissionKey NVARCHAR(200)    NOT NULL,
    CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, PermissionKey)
);

CREATE TABLE dbo.UserAccounts (
    Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_UserAccounts PRIMARY KEY,
    TenantId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_UserAccounts_Tenants REFERENCES dbo.Tenants(Id),
    EmpCode          NVARCHAR(50)     NOT NULL CONSTRAINT UQ_UserAccounts_EmpCode UNIQUE,
    FullName         NVARCHAR(200)    NOT NULL,
    Email            NVARCHAR(256)    NOT NULL CONSTRAINT UQ_UserAccounts_Email UNIQUE,
    PhoneCountryCode NVARCHAR(5)      NULL,
    PhoneNumber      NVARCHAR(15)     NULL,
    Department       NVARCHAR(200)    NOT NULL,
    Designation      NVARCHAR(200)    NOT NULL,
    Location         NVARCHAR(50)     NOT NULL,
    Region           NVARCHAR(50)     NOT NULL,
    EmploymentType   NVARCHAR(20)     NOT NULL,
    Status           NVARCHAR(20)     NOT NULL,
    MfaEnabled       BIT              NOT NULL,
    LastLogin        DATETIME2        NULL,
    AccountExpiry    DATE             NULL,
    PasswordHash     NVARCHAR(500)    NULL
);
CREATE INDEX IX_UserAccounts_Tenant ON dbo.UserAccounts (TenantId, EmpCode);

-- ABAC subject attributes (user.<key>)
CREATE TABLE dbo.UserAttributes (
    UserId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_UserAttributes_UserAccounts REFERENCES dbo.UserAccounts(Id) ON DELETE CASCADE,
    AttributeKey   NVARCHAR(100)    NOT NULL,
    AttributeValue NVARCHAR(500)    NOT NULL,
    CONSTRAINT PK_UserAttributes PRIMARY KEY (UserId, AttributeKey)
);

-- role given to a user, optionally scoped (ScopeJson e.g. {"store":"S045,S046"}) and time-bound
CREATE TABLE dbo.UserRoleAssignments (
    UserId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_UserRoleAssignments_UserAccounts REFERENCES dbo.UserAccounts(Id) ON DELETE CASCADE,
    RoleId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_UserRoleAssignments_Roles REFERENCES dbo.Roles(Id),
    ScopeJson  NVARCHAR(1000)   NOT NULL,
    ValidFrom  DATE             NULL,
    ValidTo    DATE             NULL,
    AssignedBy NVARCHAR(200)    NOT NULL,
    AssignedOn DATE             NOT NULL,
    CONSTRAINT PK_UserRoleAssignments PRIMARY KEY (UserId, RoleId)
);
CREATE INDEX IX_UserRoleAssignments_Role ON dbo.UserRoleAssignments (RoleId);

-- ABAC policies. Conditions are stored structured (JSON), never as executable expressions.
CREATE TABLE dbo.Policies (
    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Policies PRIMARY KEY,
    TenantId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Policies_Tenants REFERENCES dbo.Tenants(Id),
    Name           NVARCHAR(200)    NOT NULL,
    Description    NVARCHAR(1000)   NOT NULL,
    Effect         NVARCHAR(20)     NOT NULL,      -- Restrict | Deny | Grant
    TargetApp      NVARCHAR(30)     NOT NULL,
    TargetModule   NVARCHAR(50)     NOT NULL,
    TargetResource NVARCHAR(50)     NOT NULL,
    TargetActions  NVARCHAR(200)    NOT NULL,      -- comma separated or *
    TargetRoles    NVARCHAR(1000)   NOT NULL,      -- comma separated role codes, empty = everyone
    [Match]        NVARCHAR(10)     NOT NULL,      -- All | Any
    ConditionsJson NVARCHAR(MAX)    NOT NULL,
    Priority       INT              NOT NULL,
    IsActive       BIT              NOT NULL,
    CreatedBy      NVARCHAR(200)    NOT NULL,
    CreatedAt      DATETIME2        NOT NULL
);
CREATE INDEX IX_Policies_Tenant ON dbo.Policies (TenantId, Priority);

CREATE TABLE dbo.Schedules (
    Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Schedules PRIMARY KEY,
    TenantId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Schedules_Tenants REFERENCES dbo.Tenants(Id),
    Code            NVARCHAR(50)     NOT NULL,
    Name            NVARCHAR(200)    NOT NULL,
    Timezone        NVARCHAR(100)    NOT NULL,
    ExcludeHolidays BIT              NOT NULL,
    WindowsJson     NVARCHAR(MAX)    NOT NULL,     -- [{"days":["Mon",...],"start":"09:30","end":"18:30"}]
    CONSTRAINT UQ_Schedules_Code UNIQUE (TenantId, Code)
);

CREATE TABLE dbo.Holidays (
    Id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Holidays PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Holidays_Tenants REFERENCES dbo.Tenants(Id),
    [Date]   DATE             NOT NULL,
    Name     NVARCHAR(200)    NOT NULL,
    Regions  NVARCHAR(500)    NOT NULL           -- comma separated, empty = all regions
);
CREATE INDEX IX_Holidays_Tenant ON dbo.Holidays (TenantId, [Date]);

CREATE TABLE dbo.AuditLogs (
    Id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY NONCLUSTERED,
    TenantId  UNIQUEIDENTIFIER NOT NULL,
    [Timestamp] DATETIME2      NOT NULL,
    Actor     NVARCHAR(200)    NOT NULL,
    Entity    NVARCHAR(100)    NOT NULL,
    EntityId  NVARCHAR(MAX)    NOT NULL,
    Action    NVARCHAR(20)     NOT NULL,
    Summary   NVARCHAR(1000)   NOT NULL,
    OldValues NVARCHAR(MAX)    NULL,
    NewValues NVARCHAR(MAX)    NULL,
    Ip        NVARCHAR(50)     NOT NULL,
    Device    NVARCHAR(300)    NOT NULL
);
CREATE CLUSTERED INDEX CIX_AuditLogs ON dbo.AuditLogs (TenantId, [Timestamp] DESC);

CREATE TABLE dbo.UserActions (
    Id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_UserActions PRIMARY KEY NONCLUSTERED,
    TenantId  UNIQUEIDENTIFIER NOT NULL,
    [Timestamp] DATETIME2      NOT NULL,
    UserId    UNIQUEIDENTIFIER NOT NULL,
    UserName  NVARCHAR(200)    NOT NULL,
    App       NVARCHAR(30)     NOT NULL,
    Route     NVARCHAR(500)    NOT NULL,
    Action    NVARCHAR(200)    NOT NULL,      -- permission key that was checked
    Decision  NVARCHAR(10)     NOT NULL,      -- Allow | Deny
    Reason    NVARCHAR(2000)   NOT NULL,
    Ip        NVARCHAR(50)     NOT NULL
);
CREATE CLUSTERED INDEX CIX_UserActions ON dbo.UserActions (TenantId, [Timestamp] DESC);

CREATE TABLE dbo.ExceptionLogs (
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ExceptionLogs PRIMARY KEY NONCLUSTERED,
    TenantId    UNIQUEIDENTIFIER NOT NULL,
    [Timestamp] DATETIME2        NOT NULL,
    App         NVARCHAR(30)     NOT NULL,
    Severity    NVARCHAR(20)     NOT NULL,
    Message     NVARCHAR(2000)   NOT NULL,
    StackTrace  NVARCHAR(MAX)    NOT NULL,
    UserName    NVARCHAR(200)    NULL,
    RequestPath NVARCHAR(500)    NOT NULL,
    Ip          NVARCHAR(50)     NOT NULL,
    Resolved    BIT              NOT NULL
);
CREATE CLUSTERED INDEX CIX_ExceptionLogs ON dbo.ExceptionLogs (TenantId, [Timestamp] DESC);
