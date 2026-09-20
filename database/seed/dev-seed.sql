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

-- Volunteers: identity and personal data are separate rows (dbo.Person, dbo.PersonDetail).
DECLARE @people TABLE (Id uniqueidentifier, FirstName nvarchar(100), LastName nvarchar(100), IsDriver bit, Committed bit);
INSERT INTO @people VALUES
    (NEWID(), N'Alex',  N'Example', 1, 1),
    (NEWID(), N'Sam',   N'Sample',  1, 1),
    (NEWID(), N'Jo',    N'Test',    1, 0),
    (NEWID(), N'Chris', N'Demo',    0, 0);

INSERT INTO dbo.Person (Id) SELECT Id FROM @people;
INSERT INTO dbo.PersonDetail (PersonId, FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed)
SELECT Id, FirstName, LastName, '1985-01-01', '2024-01-01', N'07700 900000', IsDriver, Committed FROM @people;

-- A convoy still being planned: truck list not published, so vehicles can join and leave.
DECLARE @convoy int;
INSERT INTO dbo.Convoy (Start, ExpectedEnd)
VALUES (DATEADD(DAY, 30, CAST(SYSUTCDATETIME() AS date)), DATEADD(DAY, 37, CAST(SYSUTCDATETIME() AS date)));
SET @convoy = SCOPE_IDENTITY();

INSERT INTO dbo.ConvoyRouteStop (ConvoyId, Sequence, City, Country, Postcode)
VALUES (@convoy, 1, N'Coventry', N'United Kingdom', N'CV1 0AA'),
       (@convoy, 2, N'Dover',    N'United Kingdom', N'CT16 0AA'),
       (@convoy, 3, N'Lviv',     N'Ukraine',        N'');

-- Vehicles. Transmission: 1 Manual, 2 Automatic. Fuel: 2 Diesel. InspectionStatus: 0 Pending,
-- 2 Passed — only a Passed vehicle may join a convoy.
INSERT INTO dbo.Vehicle (Vin, Plate, Brand, Model, Colour, Transmission, [Year], Fuel, WeightKg, InspectionStatus)
VALUES ('SEEDVIN0000000001', N'AB12 CDE', N'Ford',       N'Transit',  N'White', 1, 2015, 2, 2000, 2),
       ('SEEDVIN0000000002', N'FG34 HIJ', N'Volkswagen', N'Crafter',  N'Blue',  1, 2016, 2, 2100, 2),
       ('SEEDVIN0000000003', N'KL56 MNO', N'Toyota',     N'Hilux',    N'Grey',  2, 2014, 2, 1900, 0);

-- The truck list. The Hilux is left off it: it has not passed inspection, so it is the vehicle to
-- try assigning when you want to see the 409.
INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin)
VALUES (@convoy, 'SEEDVIN0000000001'),
       (@convoy, 'SEEDVIN0000000002');

-- Crew, per leg: the same pair drives both legs of the Transit, which is the ordinary case. Role 0
-- is Driver. Leg 0 is UK to Europe, 1 is Europe to Ukraine.
INSERT INTO dbo.ConvoyVehicleCrew (ConvoyId, Vin, PersonId, Leg, [Role])
SELECT @convoy, 'SEEDVIN0000000001', p.Id, l.Leg, 0
FROM (SELECT TOP 2 Id FROM @people WHERE IsDriver = 1 ORDER BY LastName) AS p
CROSS JOIN (VALUES (0), (1)) AS l (Leg);

-- Boxes waiting at the Coventry depot, not yet validated.
INSERT INTO dbo.Box (WeightKg, LocationId) VALUES (12, @coventry), (8, @coventry), (15, @london);

INSERT INTO dbo.BoxItem (Id, BoxId, Description, PropertiesJson)
SELECT NEWID(), b.Id, i.Description, i.PropertiesJson
FROM dbo.Box AS b
CROSS APPLY (VALUES (N'First aid kits', N'{"quantity":10}'),
                    (N'Thermal blankets', N'{"quantity":20}')) AS i (Description, PropertiesJson);

COMMIT;

PRINT 'Dev seed loaded.';
