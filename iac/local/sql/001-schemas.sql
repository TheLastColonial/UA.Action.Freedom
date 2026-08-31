/*
    Freedom database bootstrap — the local stand-in for what a migration would create
    on Azure SQL. Applied by OpenTofu (../../tofu/database.tf), not by the container,
    because resource creation belongs to the control plane.

    Idempotent: safe to run repeatedly — and it must also be correct on an *empty* database,
    which is what CI has. Statements run top to bottom, so nothing may reference an object
    created further down. Prefer schema-level grants, which apply to objects created after
    them and so carry no ordering constraint at all. See docs/gotchas-and-open-questions.md
    for how to reproduce a clean run locally before pushing a change here.

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
-- Logins, and the users that carry those roles
--
-- The roles and the DENY above are inert until something actually connects *as* one of
-- them. Until these logins existed the application connected as `sa` — and `sa` is
-- sysadmin, which bypasses permission checks entirely, so the DENY that 4.4 calls
-- load-bearing was decorative. Two ordinary logins fix that:
--
--   freedom_app        the application's own identity. Full DML on dbo, DENY on sensitive.
--   freedom_sensitive  the Ground Officer path, and the only way to read a delivery address.
--
-- In Azure both are managed identities with Entra-only authentication and no password at
-- all (4.2); the passwords here exist because a local SQL Server has nothing else to
-- authenticate with. They arrive as sqlcmd scripting variables resolved from the
-- environment, so they are never on a command line — same reasoning as ../../tofu/database.tf.
--
-- User names differ from role names because a database principal cannot share a name with
-- a role in the same database.
-- --------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'freedom_app')
BEGIN
    CREATE LOGIN freedom_app WITH PASSWORD = '$(FREEDOM_APP_PASSWORD)', CHECK_POLICY = OFF;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'freedom_sensitive')
BEGIN
    CREATE LOGIN freedom_sensitive WITH PASSWORD = '$(FREEDOM_SENSITIVE_PASSWORD)', CHECK_POLICY = OFF;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'freedom_app_user')
BEGIN
    CREATE USER freedom_app_user FOR LOGIN freedom_app;
    ALTER ROLE freedom_app ADD MEMBER freedom_app_user;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'freedom_sensitive_user')
BEGIN
    CREATE USER freedom_sensitive_user FOR LOGIN freedom_sensitive;
    ALTER ROLE ground_officer ADD MEMBER freedom_sensitive_user;
END
GO

-- No table-level grant is needed for the audit log the Ground Officer path writes on every
-- address it resolves: the SCHEMA::sensitive grant above already carries INSERT, and it
-- applies to objects created after it. An explicit GRANT here would also have to come after
-- the CREATE TABLE, which is several sections further down — a statement that only works on
-- a database where the table happens to exist already.

-- --------------------------------------------------------------------------
-- Receivers — the split that matters most
--
-- dbo.Receiver is what the rest of the application joins on and what may appear on a
-- document that crosses a border: an opaque reference, the organisation, and a region.
-- Region is as precise as anything that travels gets (4.4.2).
--
-- sensitive.ReceiverDetail holds the delivery address and the contact. Only ground_officer
-- can read it, and freedom_app is explicitly DENY'd, so the application identity cannot
-- select a delivery address even if someone later adds a broad grant elsewhere.
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

-- The Receiver table predates the repository convention of carrying an UpdatedAt.
IF COL_LENGTH('dbo.Receiver', 'UpdatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Receiver ADD UpdatedAt datetime2(0) NOT NULL
        CONSTRAINT DF_Receiver_UpdatedAt DEFAULT SYSUTCDATETIME();
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

-- --------------------------------------------------------------------------
-- Cargo — dbo.Box and dbo.BoxItem
--
-- A box is a packed container of items with a confirmed weight, a current location and a
-- target receiver. ValidatedByPersonId + ValidatedAt are an audit artefact rather than a
-- status flag: a Loader physically checks the contents and weighs the box, and that check
-- is the trust boundary between the donor and Ukrainian Action. The application refuses to
-- change a validated box, so those two columns are only ever written once.
--
-- ReceiverRef is the opaque reference into dbo.Receiver and nothing more. The delivery
-- address lives in sensitive.ReceiverDetail and never comes near cargo (4.4).
--
-- Location is the box's current whereabouts — a UK depot, routine, not sensitive.
--
-- Item properties are open-ended (size, condition, expiry, whatever a donation needs), so
-- they are stored as a JSON document rather than as a table nobody could keep up with.
-- --------------------------------------------------------------------------

IF OBJECT_ID('dbo.Box') IS NULL
BEGIN
    CREATE TABLE dbo.Box (
        Id                  int              NOT NULL IDENTITY(1,1) CONSTRAINT PK_Box PRIMARY KEY,
        WeightKg            int              NOT NULL CONSTRAINT DF_Box_WeightKg DEFAULT 0,
        ReceiverRef         uniqueidentifier NULL,
        House               nvarchar(100)    NULL,
        Street              nvarchar(200)    NULL,
        City                nvarchar(100)    NULL,
        Country             nvarchar(100)    NULL,
        Postcode            nvarchar(20)     NULL,
        ValidatedByPersonId uniqueidentifier NULL,
        ValidatedAt         datetime2(0)     NULL,
        CreatedAt           datetime2(0)     NOT NULL CONSTRAINT DF_Box_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt           datetime2(0)     NOT NULL CONSTRAINT DF_Box_UpdatedAt DEFAULT SYSUTCDATETIME(),

        -- A receiver cannot be deleted out from under cargo already routed to it.
        CONSTRAINT FK_Box_Receiver FOREIGN KEY (ReceiverRef) REFERENCES dbo.Receiver (ReceiverRef),

        -- The validator is a volunteer on file. NO ACTION on delete: a volunteer who leaves
        -- must not take the record of what they signed for with them.
        CONSTRAINT FK_Box_ValidatedBy FOREIGN KEY (ValidatedByPersonId) REFERENCES dbo.Person (Id),

        -- Validation is one event, so its two halves are written together or not at all.
        CONSTRAINT CK_Box_ValidationIsWholeOrAbsent CHECK (
            (ValidatedByPersonId IS NULL AND ValidatedAt IS NULL)
            OR (ValidatedByPersonId IS NOT NULL AND ValidatedAt IS NOT NULL))
    );
END
GO

IF OBJECT_ID('dbo.BoxItem') IS NULL
BEGIN
    CREATE TABLE dbo.BoxItem (
        Id             uniqueidentifier NOT NULL CONSTRAINT PK_BoxItem PRIMARY KEY,
        BoxId          int              NOT NULL,
        Description    nvarchar(400)    NOT NULL,
        PropertiesJson nvarchar(max)    NOT NULL CONSTRAINT DF_BoxItem_PropertiesJson DEFAULT '{}',

        -- Items have no life outside their box: unpacking one is deleting the box.
        CONSTRAINT FK_BoxItem_Box FOREIGN KEY (BoxId) REFERENCES dbo.Box (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_BoxItem_BoxId ON dbo.BoxItem (BoxId);
END
GO

-- --------------------------------------------------------------------------
-- Manifests — dbo.Manifest, dbo.ManifestDriverTeam and dbo.ManifestBox
--
-- The central document of the system: one vehicle, on one convoy, with its two driver teams
-- and its cargo. ManifestId is a natural key like Vin — it is a document reference people
-- read out at a border, not a surrogate.
--
-- Status is the ten-state model of docs/manifest-status.puml, stored as int to line up with
-- the CLR enum Dapper hydrates. The legal edges live in ManifestTransitions, not here: a
-- CHECK constraint would have to be kept in step with the code by hand, and the code is
-- where the two rules the diagram cannot express already live.
--
-- GmrSubmittedAt is the freeze. recommendations 5.2 records the ruling that once a GMR is
-- created no edit may modify the manifest; the application refuses every write once this is
-- set, and only Delivered / Lost / Returned remain reachable.
-- --------------------------------------------------------------------------

IF OBJECT_ID('dbo.Manifest') IS NULL
BEGIN
    CREATE TABLE dbo.Manifest (
        Id                   varchar(32)   NOT NULL CONSTRAINT PK_Manifest PRIMARY KEY,
        Vin                  varchar(32)   NULL,
        ConvoyId             int           NULL,
        Status               int           NOT NULL CONSTRAINT DF_Manifest_Status DEFAULT 0,
        DeliveryNotes        nvarchar(2000) NULL,
        FerryBookingComplete bit           NOT NULL CONSTRAINT DF_Manifest_Ferry DEFAULT 0,
        GmrSubmittedAt       datetime2(0)  NULL,
        CreatedAt            datetime2(0)  NOT NULL CONSTRAINT DF_Manifest_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt            datetime2(0)  NOT NULL CONSTRAINT DF_Manifest_UpdatedAt DEFAULT SYSUTCDATETIME(),

        -- A vehicle or convoy cannot be deleted out from under a manifest that names it.
        CONSTRAINT FK_Manifest_Vehicle FOREIGN KEY (Vin) REFERENCES dbo.Vehicle (Vin),
        CONSTRAINT FK_Manifest_Convoy FOREIGN KEY (ConvoyId) REFERENCES dbo.Convoy (Id)
    );
END
GO

-- One team per leg: 0 is UK to Europe, 1 is Europe to Ukraine. The pair is the primary key,
-- so assigning a team to a leg replaces whoever was on it rather than accumulating crews.
IF OBJECT_ID('dbo.ManifestDriverTeam') IS NULL
BEGIN
    CREATE TABLE dbo.ManifestDriverTeam (
        ManifestId        varchar(32)      NOT NULL,
        Leg               int              NOT NULL,
        PrimaryPersonId   uniqueidentifier NOT NULL,
        SecondaryPersonId uniqueidentifier NULL,

        CONSTRAINT PK_ManifestDriverTeam PRIMARY KEY (ManifestId, Leg),
        CONSTRAINT FK_ManifestDriverTeam_Manifest FOREIGN KEY (ManifestId)
            REFERENCES dbo.Manifest (Id) ON DELETE CASCADE,
        CONSTRAINT FK_ManifestDriverTeam_Primary FOREIGN KEY (PrimaryPersonId) REFERENCES dbo.Person (Id),
        CONSTRAINT FK_ManifestDriverTeam_Secondary FOREIGN KEY (SecondaryPersonId) REFERENCES dbo.Person (Id),

        -- A pair is two people. The same volunteer twice would read as crewed while leaving
        -- somebody driving a leg to Ukraine alone.
        CONSTRAINT CK_ManifestDriverTeam_DistinctDrivers CHECK (
            SecondaryPersonId IS NULL OR SecondaryPersonId <> PrimaryPersonId)
    );
END
GO

-- Cargo. A box travels on at most one manifest, which the primary key on BoxId enforces:
-- the same box on two manifests would be counted twice at a border and arrive once.
IF OBJECT_ID('dbo.ManifestBox') IS NULL
BEGIN
    CREATE TABLE dbo.ManifestBox (
        BoxId      int         NOT NULL CONSTRAINT PK_ManifestBox PRIMARY KEY,
        ManifestId varchar(32) NOT NULL,

        CONSTRAINT FK_ManifestBox_Manifest FOREIGN KEY (ManifestId)
            REFERENCES dbo.Manifest (Id) ON DELETE CASCADE,
        -- Removing a box from the system takes it off the manifest with it.
        CONSTRAINT FK_ManifestBox_Box FOREIGN KEY (BoxId)
            REFERENCES dbo.Box (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_ManifestBox_ManifestId ON dbo.ManifestBox (ManifestId);
END
GO

PRINT 'Freedom database bootstrap complete.';
GO
