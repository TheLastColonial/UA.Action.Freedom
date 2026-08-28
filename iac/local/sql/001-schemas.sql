/*
    Freedom database bootstrap — the local stand-in for what a migration would create
    on Azure SQL. Applied by OpenTofu (../../tofu/database.tf), not by the container,
    because resource creation belongs to the control plane.

    Idempotent: safe to run repeatedly.

    The point of this file is not the tables — those arrive with the Data project. It is
    the *shape* of the access control, so that docs/recommendations.md 4.4 is something
    the application is tested against rather than a paragraph of intent:

      dbo.*        convoy logistics — every role reads it
      sensitive.*  Ukrainian delivery addresses and receiver contacts — Ground Officer only

    A manifest listing precise delivery addresses is a targeting document and it crosses
    borders where it may be inspected or seized. The separation is enforced by the
    database, so that widening it takes a deliberate, reviewable change here.
*/

IF DB_ID('Freedom') IS NULL
BEGIN
    CREATE DATABASE Freedom;
END
GO

USE Freedom;
GO

-- --------------------------------------------------------------------------
-- Schemas
-- --------------------------------------------------------------------------

IF SCHEMA_ID('sensitive') IS NULL
    EXEC('CREATE SCHEMA sensitive AUTHORIZATION dbo;');
GO

-- --------------------------------------------------------------------------
-- Roles
--
-- In Azure these map to Entra groups holding the app roles from key-concepts.md,
-- with Entra-only authentication on the server (4.2). Locally they are plain
-- database roles: the grants are what the application code has to satisfy, and
-- those are identical either way.
-- --------------------------------------------------------------------------

DECLARE @roles TABLE (name sysname);
INSERT INTO @roles (name) VALUES
    ('freedom_app'),        -- the Freedom Application's own identity
    ('freedom_worker'),     -- the Customs Worker's own identity
    ('administrator'),
    ('dispatcher'),
    ('loader'),
    ('purchaser'),
    ('ground_officer');

DECLARE @role sysname, @sql nvarchar(max);
DECLARE role_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @roles;
OPEN role_cursor;
FETCH NEXT FROM role_cursor INTO @role;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF DATABASE_PRINCIPAL_ID(@role) IS NULL
    BEGIN
        SET @sql = N'CREATE ROLE ' + QUOTENAME(@role) + N';';
        EXEC sp_executesql @sql;
    END
    FETCH NEXT FROM role_cursor INTO @role;
END
CLOSE role_cursor;
DEALLOCATE role_cursor;
GO

-- --------------------------------------------------------------------------
-- Grants on convoy logistics — the non-sensitive half
-- --------------------------------------------------------------------------

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO freedom_app;
GRANT SELECT, INSERT, UPDATE          ON SCHEMA::dbo TO freedom_worker;
GRANT SELECT                          ON SCHEMA::dbo TO administrator, dispatcher, loader, purchaser, ground_officer;
GO

-- --------------------------------------------------------------------------
-- Grants on delivery detail — Ground Officer only
--
-- The DENY is the load-bearing line. Without it, membership of two roles would
-- silently combine to grant access; DENY overrides GRANT in SQL Server, so the
-- application identity cannot read receiver addresses even if someone later adds
-- a broad grant elsewhere. Removing it is the thing to review for.
-- --------------------------------------------------------------------------

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::sensitive TO ground_officer;
DENY SELECT ON SCHEMA::sensitive TO freedom_app, freedom_worker, administrator, dispatcher, loader, purchaser;
GO

-- --------------------------------------------------------------------------
-- A table in each schema, so the separation is demonstrable before the real
-- model lands. Replace these when the Data project grows migrations.
-- --------------------------------------------------------------------------

IF OBJECT_ID('dbo.Receiver') IS NULL
BEGIN
    CREATE TABLE dbo.Receiver (
        -- The opaque reference the rest of the application joins on. Carries no
        -- addressing information: region is as precise as anything that travels gets.
        ReceiverRef     uniqueidentifier NOT NULL CONSTRAINT PK_Receiver PRIMARY KEY,
        Organisation    nvarchar(200)    NOT NULL,
        Region          nvarchar(100)    NOT NULL,
        CreatedAt       datetime2(0)     NOT NULL CONSTRAINT DF_Receiver_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('sensitive.ReceiverDetail') IS NULL
BEGIN
    CREATE TABLE sensitive.ReceiverDetail (
        ReceiverRef     uniqueidentifier NOT NULL CONSTRAINT PK_ReceiverDetail PRIMARY KEY
                        CONSTRAINT FK_ReceiverDetail_Receiver REFERENCES dbo.Receiver (ReceiverRef),
        ContactName     nvarchar(200)    NOT NULL,
        ContactPhone    nvarchar(50)     NOT NULL,
        AddressLine1    nvarchar(200)    NOT NULL,
        AddressLine2    nvarchar(200)    NULL,
        City            nvarchar(100)    NOT NULL,
        PostCode        nvarchar(20)     NULL,
        -- 4.4.5: delete this row a defined period after delivery is confirmed.
        DeleteAfter     datetime2(0)     NULL
    );
END
GO

-- Every read of a full address is audited (4.4.3). This matters more than the data.
IF OBJECT_ID('sensitive.ReceiverDetailAccessLog') IS NULL
BEGIN
    CREATE TABLE sensitive.ReceiverDetailAccessLog (
        Id              bigint           IDENTITY(1,1) CONSTRAINT PK_ReceiverDetailAccessLog PRIMARY KEY,
        ReceiverRef     uniqueidentifier NOT NULL,
        PrincipalId     nvarchar(200)    NOT NULL,
        ReadAt          datetime2(0)     NOT NULL CONSTRAINT DF_ReceiverDetailAccessLog_ReadAt DEFAULT SYSUTCDATETIME(),
        Reason          nvarchar(400)    NULL
    );
END
GO

PRINT 'Freedom database bootstrap complete.';
GO
