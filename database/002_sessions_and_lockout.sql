/*
  VMM User Management System - sign-in sessions, refresh tokens and lockout (SQL Server 2019+)
  Run after 001_create_access_control_schema.sql (also on an existing UMS database).
  Refresh tokens are never stored: RefreshTokenHash is SHA-256 of the token (hex).
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required for the filtered index (sqlcmd defaults to OFF)

IF OBJECT_ID('dbo.UserSessions') IS NULL
CREATE TABLE dbo.UserSessions (
    Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_UserSessions PRIMARY KEY NONCLUSTERED,
    TenantId         UNIQUEIDENTIFIER NOT NULL,
    UserId           UNIQUEIDENTIFIER NOT NULL,
    UserName         NVARCHAR(200)    NOT NULL,
    EmpCode          NVARCHAR(30)     NOT NULL,
    AppCode          NVARCHAR(30)     NOT NULL,      -- single-device login is enforced per user and application
    IpAddress        NVARCHAR(50)     NOT NULL,
    UserAgent        NVARCHAR(300)    NOT NULL,
    CreatedAt        DATETIME2        NOT NULL,      -- UTC
    ExpiresAt        DATETIME2        NOT NULL,      -- UTC, absolute end (RefreshTokenDays)
    LastSeenAt       DATETIME2        NOT NULL,      -- UTC, drives the idle timeout
    RefreshTokenHash CHAR(64)         NOT NULL,
    EndedAt          DATETIME2        NULL,
    EndReason        NVARCHAR(20)     NOT NULL CONSTRAINT DF_UserSessions_EndReason DEFAULT ('None')  -- SignedOut | Replaced | Idle | ...
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'CIX_UserSessions')
    CREATE CLUSTERED INDEX CIX_UserSessions ON dbo.UserSessions (TenantId, CreatedAt DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserSessions_Open')
    CREATE INDEX IX_UserSessions_Open ON dbo.UserSessions (UserId, AppCode) WHERE EndReason = 'None';

IF OBJECT_ID('dbo.LoginAttempts') IS NULL
CREATE TABLE dbo.LoginAttempts (
    Login       NVARCHAR(256) NOT NULL CONSTRAINT PK_LoginAttempts PRIMARY KEY,   -- lower-case email
    FailedCount INT           NOT NULL,
    LockedUntil DATETIME2     NULL                                                  -- UTC
);
