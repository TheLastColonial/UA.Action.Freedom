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

-- --------------------------------------------------------------------------
-- Convoy logistics — dbo.Vehicle
--
-- The first table from the Data project. VIN is the natural key. Navigation to
-- Convoy / Purchaser / Drivers is not modelled yet: ConvoyId is a loose int and
-- PurchaserName a denormalised string until dbo.Convoy and a people table exist.
-- freedom_app already holds full DML on SCHEMA::dbo (above), so no new grant.
--
-- Column types are int (not tinyint/smallint) for the enum and year fields so they
-- line up with the CLR types Dapper's constructor mapping expects for VehicleReadModel.
-- --------------------------------------------------------------------------

IF OBJECT_ID('dbo.Vehicle') IS NULL
BEGIN
    CREATE TABLE dbo.Vehicle (
        Vin           varchar(32)    NOT NULL CONSTRAINT PK_Vehicle PRIMARY KEY,
        Plate         nvarchar(16)   NOT NULL,
        Brand         nvarchar(64)   NULL,
        Model         nvarchar(64)   NULL,
        Colour        nvarchar(32)   NULL,
        Transmission  int            NOT NULL CONSTRAINT DF_Vehicle_Transmission DEFAULT 0,
        Notes         nvarchar(1000) NULL,
        Mileage       int            NULL,
        Servicing     bit            NOT NULL CONSTRAINT DF_Vehicle_Servicing DEFAULT 0,
        [Year]        int            NOT NULL,
        Fuel          int            NOT NULL CONSTRAINT DF_Vehicle_Fuel DEFAULT 0,
        ConvoyId      int            NULL,
        PurchaserName nvarchar(200)  NULL,
        PurchaseDate  datetime2(0)   NULL,
        WeightKg      int            NOT NULL CONSTRAINT DF_Vehicle_WeightKg DEFAULT 0,
        CreatedAt     datetime2(0)   NOT NULL CONSTRAINT DF_Vehicle_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt     datetime2(0)   NOT NULL CONSTRAINT DF_Vehicle_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

-- --------------------------------------------------------------------------
-- Volunteers — dbo.Person
--
-- One row per individual supporting Ukrainian Action. The domain models a Driver as a
-- subtype of Person; the database keeps one table with IsDriver telling them apart, because
-- everything a driver adds (Committed) is two columns rather than a second identity.
--
-- Personal data (recommendations 4.8): UK residency, never written to a log, and a defined
-- retention period. The key is a uniqueidentifier rather than an IDENTITY sequence so that a
-- volunteer's URL does not disclose how many volunteers the charity has.
--
-- Convoy history (Driver.Convoys) is not modelled yet — it arrives with dbo.Convoy.
-- freedom_app already holds full DML on SCHEMA::dbo (above), so no new grant.
-- --------------------------------------------------------------------------

IF OBJECT_ID('dbo.Person') IS NULL
BEGIN
    CREATE TABLE dbo.Person (
        Id           uniqueidentifier NOT NULL CONSTRAINT PK_Person PRIMARY KEY,
        FirstName    nvarchar(100)    NOT NULL,
        LastName     nvarchar(100)    NOT NULL,
        DateOfBirth  datetime2(0)     NOT NULL,
        Joined       datetime2(0)     NOT NULL,
        Phone        nvarchar(50)     NULL,
        IsDriver     bit              NOT NULL CONSTRAINT DF_Person_IsDriver DEFAULT 0,
        Committed    bit              NOT NULL CONSTRAINT DF_Person_Committed DEFAULT 0,
        CreatedAt    datetime2(0)     NOT NULL CONSTRAINT DF_Person_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt    datetime2(0)     NOT NULL CONSTRAINT DF_Person_UpdatedAt DEFAULT SYSUTCDATETIME()
    );

    -- The dispatcher's shortlist is "drivers, by name". Everything else pages the full roster
    -- in the same order, so one index serves both reads.
    CREATE INDEX IX_Person_IsDriver_Name ON dbo.Person (IsDriver, LastName, FirstName, Id);
END
GO

-- --------------------------------------------------------------------------
-- Convoys — dbo.Convoy and dbo.ConvoyRouteStop
--
-- A convoy is the unit that is planned; the manifest is the unit that is executed per
-- vehicle. Roughly one convoy a month, never concurrent (recommendations 5.2), so an int
-- IDENTITY is a generous key and it keeps the /convoys/{id} route readable.
--
-- TruckListPublishedAt is the gate in docs/process.puml: Truck List Created -> Truck List
-- Published -> Manifest Proposed. Manifests are proposed against the published set of
-- vehicles, so publication is one-way and the application refuses to change the vehicle
-- list afterwards. NULL means "still being planned".
--
-- The route is a child table rather than a column of stops, because Sequence is what makes
-- it a journey rather than a bag of addresses. Sequence is dense and 1-based; the
-- application renumbers on write, so nothing here has to trust the caller's numbering.
-- --------------------------------------------------------------------------

IF OBJECT_ID('dbo.Convoy') IS NULL
BEGIN
    CREATE TABLE dbo.Convoy (
        Id                   int          NOT NULL IDENTITY(1,1) CONSTRAINT PK_Convoy PRIMARY KEY,
        Start                datetime2(0) NOT NULL,
        ExpectedEnd          datetime2(0) NOT NULL,
        TruckListPublishedAt datetime2(0) NULL,
        CreatedAt            datetime2(0) NOT NULL CONSTRAINT DF_Convoy_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt            datetime2(0) NOT NULL CONSTRAINT DF_Convoy_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.ConvoyRouteStop') IS NULL
BEGIN
    CREATE TABLE dbo.ConvoyRouteStop (
        ConvoyId  int           NOT NULL,
        Sequence  int           NOT NULL,
        House     nvarchar(100) NULL,
        Street    nvarchar(200) NULL,
        City      nvarchar(100) NULL,
        Country   nvarchar(100) NULL,
        Postcode  nvarchar(20)  NOT NULL CONSTRAINT DF_ConvoyRouteStop_Postcode DEFAULT '',
        CONSTRAINT PK_ConvoyRouteStop PRIMARY KEY (ConvoyId, Sequence),
        -- The route has no life of its own: deleting the convoy takes it with it, which is
        -- also what stops a cancelled convoy leaving orphan stops behind.
        CONSTRAINT FK_ConvoyRouteStop_Convoy FOREIGN KEY (ConvoyId)
            REFERENCES dbo.Convoy (Id) ON DELETE CASCADE
    );
END
GO

-- --------------------------------------------------------------------------
-- dbo.Vehicle.ConvoyId becomes a real foreign key
--
-- The Vehicle table was created before dbo.Convoy existed, and its own comment records
-- ConvoyId as "a loose int ... until dbo.Convoy exists". It does now.
--
-- Any value already in that column was written when nothing validated it, so orphans are
-- cleared first — the constraint cannot be added while they are there, and this script has
-- to stay re-runnable on a database that predates the convoy table. ON DELETE SET NULL:
-- cancelling a convoy releases its vehicles rather than deleting donated vehicles.
-- --------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Vehicle_Convoy')
BEGIN
    UPDATE v SET ConvoyId = NULL
    FROM dbo.Vehicle AS v
    WHERE v.ConvoyId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.Convoy AS c WHERE c.Id = v.ConvoyId);

    ALTER TABLE dbo.Vehicle ADD CONSTRAINT FK_Vehicle_Convoy
        FOREIGN KEY (ConvoyId) REFERENCES dbo.Convoy (Id) ON DELETE SET NULL;
END
GO

-- The truck list is read by convoy: "which vehicles are travelling together".
--
-- Deliberately not a filtered index on ConvoyId IS NOT NULL, though most vehicles are
-- unassigned between convoys. Filtered indexes require SET QUOTED_IDENTIFIER ON, and sqlcmd
-- — which is what applies this file — runs with it off. A bootstrap script that depends on
-- the session settings of whatever tool invokes it is a trap; the fleet is small enough that
-- the saving was never worth it.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Vehicle_ConvoyId' AND object_id = OBJECT_ID('dbo.Vehicle'))
BEGIN
    CREATE INDEX IX_Vehicle_ConvoyId ON dbo.Vehicle (ConvoyId);
END
GO

PRINT 'Freedom database bootstrap complete.';
GO
