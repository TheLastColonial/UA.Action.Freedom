/*
    Local dev seed — fictional data to explore after the stack comes up.

    Opt-in via the separate db-seed compose service (`docker compose up db-seed`), never part of
    the dacpac, never run in CI, never run outside a local stack. Integration and BDD tests create
    the data they need and must not find someone else's.

    Every value is fictional. There is deliberately nothing in sensitive.* — a real-looking
    Ukrainian delivery address is exactly what this system exists to keep out of files like this
    one. Create receiver detail through the API as the groundofficer login if you need it.

    Safe to run multiple times — the script guards against re-seeding an already-populated database.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.Location) OR EXISTS (SELECT 1 FROM dbo.Vehicle) OR EXISTS (SELECT 1 FROM dbo.Convoy)
BEGIN
    PRINT 'Dev seed skipped: the database already holds data.';
    RETURN;
END

BEGIN TRANSACTION;

-- Depots and their bays.
DECLARE @coventry int, @london int;

INSERT INTO dbo.Location (Name, Street, City, Country, Postcode)
VALUES (N'Coventry Depot', N'1 Example Road', N'Coventry', N'United Kingdom', N'CV1 0AA');
SET @coventry = SCOPE_IDENTITY();

INSERT INTO dbo.Location (Name, Street, City, Country, Postcode)
VALUES (N'London Depot', N'2 Sample Street', N'London', N'United Kingdom', N'E1 0AA');
SET @london = SCOPE_IDENTITY();

INSERT INTO dbo.Bay (LocationId, Code)
VALUES (@coventry, N'A1'), (@coventry, N'A2'), (@coventry, N'B1'), (@london, N'A1');

-- Receivers: dbo.Receiver only -- organisation and region, never an address (that is sensitive.*).
-- Status 0 is pending, 1 registered. A box or a vehicle can name only a registered one, so there are two
-- of those to use and a pending one to see the refusal with. The seed registers them directly; through
-- the API only an Administrator can.
DECLARE @aidHospital uniqueidentifier = NEWID(), @reliefCharity uniqueidentifier = NEWID();

INSERT INTO dbo.Receiver (ReceiverRef, Organisation, Region, [Status])
VALUES (@aidHospital,   N'Example Aid Hospital',    N'Example oblast', 1),
       (@reliefCharity, N'Sample Relief Charity',   N'Sample oblast',  1),
       (NEWID(),        N'Test Clinic (pending)',   N'Test oblast',    0);

-- Volunteers: identity and personal data are separate rows (dbo.Person, dbo.PersonDetail).
DECLARE @people TABLE (Id uniqueidentifier, FirstName nvarchar(100), LastName nvarchar(100), IsDriver bit, Committed bit);
INSERT INTO @people VALUES
    (NEWID(), N'Alex',  N'Example', 1, 1),
    (NEWID(), N'Sam',   N'Sample',  1, 1),
    (NEWID(), N'Jo',    N'Test',    1, 0),
    (NEWID(), N'Chris', N'Demo',    0, 0),
    -- One volunteer per seed login (admin, operator, groundofficer). Deliberately unlinked: a login's
    -- subject is generated when the Keycloak realm is imported, so it cannot be seeded. Link them as
    -- the admin login, see docs/local-authentication.md § Linking a login to a volunteer.
    (NEWID(), N'Ada',   N'Admin',    0, 0),
    (NEWID(), N'Olly',  N'Operator', 0, 0),
    (NEWID(), N'Gus',   N'Ground',   0, 0);

INSERT INTO dbo.Person (Id) SELECT Id FROM @people;
INSERT INTO dbo.PersonDetail (PersonId, FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed)
SELECT Id, FirstName, LastName, '1985-01-01', '2024-01-01', N'07700 900000', IsDriver, Committed FROM @people;

-- A convoy still being planned: truck list not published, so vehicles can join and leave.
DECLARE @convoy int;
-- CrossingMode 0 is Ferry, 1 is Shuttle. A ferry crossing is declared maritime on an ICS2 ENS and
-- names the vessel as its active means of transport, so it needs an IMO -- which is why this one
-- carries a real one from the Dover-Calais route rather than leaving the filing sheet incomplete.
INSERT INTO dbo.Convoy (Start, ExpectedEnd, CrossingMode, VesselImo)
VALUES (DATEADD(DAY, 30, CAST(SYSUTCDATETIME() AS date)), DATEADD(DAY, 37, CAST(SYSUTCDATETIME() AS date)),
        0, '9245779');
SET @convoy = SCOPE_IDENTITY();

-- CountryCode is the ISO alpha-2 an ENS declares its countries of routing as. Country stays free
-- text, because a dispatcher writes it; missing a transit country stops EU customs completing its
-- pre-arrival risk assessment, so the seeded route names every one it passes through.
INSERT INTO dbo.ConvoyRouteStop (ConvoyId, Sequence, City, Country, Postcode, CountryCode)
VALUES (@convoy, 1, N'Coventry', N'United Kingdom', N'CV1 0AA',  'GB'),
       (@convoy, 2, N'Dover',    N'United Kingdom', N'CT16 0AA', 'GB'),
       (@convoy, 3, N'Calais',   N'France',         N'',         'FR'),
       (@convoy, 4, N'Poznan',   N'Poland',         N'',         'PL'),
       (@convoy, 5, N'Lviv',     N'Ukraine',        N'',         'UA');

-- Vehicles. Transmission: 1 Manual, 2 Automatic. Fuel: 2 Diesel. InspectionStatus: 0 Pending,
-- 2 Passed — only a Passed vehicle may join a convoy.
INSERT INTO dbo.Vehicle (Vin, Plate, Brand, Model, Colour, Transmission, [Year], Fuel, WeightKg, InspectionStatus)
VALUES ('SEEDVIN0000000001', N'AB12 CDE', N'Ford',       N'Transit',  N'White', 1, 2015, 2, 2000, 2),
       ('SEEDVIN0000000002', N'FG34 HIJ', N'Volkswagen', N'Crafter',  N'Blue',  1, 2016, 2, 2100, 2),
       ('SEEDVIN0000000003', N'KL56 MNO', N'Toyota',     N'Hilux',    N'Grey',  2, 2014, 2, 1900, 0);

-- The truck list. The Hilux is left off it: it has not passed inspection, so it is the vehicle to
-- try assigning when you want to see the 409.
INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin, HandoverReceiverRef)
VALUES (@convoy, 'SEEDVIN0000000001', @aidHospital),
       (@convoy, 'SEEDVIN0000000002', NULL);

-- Crew: one seat per person per convoy. Role 0 is Driver.
INSERT INTO dbo.ConvoyVehicleCrew (ConvoyId, Vin, PersonId, [Role])
SELECT @convoy, 'SEEDVIN0000000001', p.Id, 0
FROM (SELECT TOP 2 Id FROM @people WHERE IsDriver = 1 ORDER BY LastName) AS p;

-- Boxes waiting at the depots, not yet validated, each addressed to a registered receiver.
INSERT INTO dbo.Box (WeightKg, LocationId, ReceiverRef)
VALUES (12, @coventry, @aidHospital), (8, @coventry, @aidHospital), (15, @london, @reliefCharity);

-- CommodityCode 99190000 is goods for humanitarian relief (issue #24), which is what an ICS2 ENS
-- declares this cargo under. Seeded so a filing sheet from local data reports nothing missing --
-- an unclassified item is reported by description, and it is worth seeing that path deliberately
-- rather than on every box.
INSERT INTO dbo.BoxItem (Id, BoxId, Description, PropertiesJson, CommodityCode)
SELECT NEWID(), b.Id, i.Description, i.PropertiesJson, i.CommodityCode
FROM dbo.Box AS b
CROSS APPLY (VALUES (N'First aid kits',   N'{"quantity":10}', '99190000'),
                    (N'Thermal blankets', N'{"quantity":20}', '99190000')) AS i (Description, PropertiesJson, CommodityCode);

COMMIT;

PRINT 'Dev seed loaded.';
