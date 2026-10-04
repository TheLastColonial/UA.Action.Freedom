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

-- Item categories (ADR 0014). The fixed list a Loader picks from. IsFixed = 1 is only ever set here: no request can make
-- a category built in. HazardClass is the ADR class; IsNotCarried marks goods the convoy will not take (gas, lithium
-- batteries, flammables, D21); WarnWithinDays is how close to expiry an item counts as short-dated. Those thresholds are
-- UNVERIFIED (D25) and the codes below are ILLUSTRATIVE six-digit headings, not a classification anyone has checked --
-- an Administrator maintains both through the API (categories:write). 99190000 is goods for humanitarian relief (#24).
INSERT INTO dbo.ItemCategory (NameEn, IsFixed, HazardClass, IsSensitive, IsNotCarried, WarnWithinDays)
VALUES (N'Medicine',        1, NULL, 1, 0, 180),
       (N'Food',            1, NULL, 0, 0, 90),
       (N'Clothing',        1, NULL, 0, 0, NULL),
       (N'Hygiene',         1, NULL, 0, 0, NULL),
       (N'Medical devices', 1, NULL, 0, 0, NULL),
       (N'Tools',           1, NULL, 0, 0, NULL),
       (N'Batteries',       1, 9,    0, 1, NULL),
       (N'Gas',             1, 2,    0, 1, NULL),
       (N'Flammables',      1, 3,    0, 1, NULL),
       (N'Other',           1, NULL, 0, 0, NULL);

-- Authority 1 is the EU, 0 the UK, 2 Ukraine. Only the EU is mapped here, because the ENS filing sheet is what reads it.
INSERT INTO dbo.CategoryCustomsCode (CategoryId, Authority, Code)
SELECT c.Id, 1, m.Code
FROM dbo.ItemCategory AS c
INNER JOIN (VALUES (N'Medicine', '300490'), (N'Food', '210690'), (N'Clothing', '630900'),
                   (N'Hygiene', '340111'), (N'Medical devices', '901890'), (N'Tools', '820559'),
                   (N'Batteries', '850780'), (N'Other', '99190000')) AS m (NameEn, Code) ON m.NameEn = c.NameEn;

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

-- Donors are a split identity too (dbo.Donor, dbo.DonorDetail). Fictional names and example.org addresses only.
DECLARE @donorA uniqueidentifier = NEWID(), @donorB uniqueidentifier = NEWID();
INSERT INTO dbo.Donor (Id) VALUES (@donorA), (@donorB);
INSERT INTO dbo.DonorDetail (DonorId, Name, Email, Phone)
VALUES (@donorA, N'Margaret Example', N'margaret@example.org', N'07700 900111'),
       (@donorB, N'Riverside Community Church', N'office@example.org', NULL);

DECLARE @donationA int, @donationB int;
INSERT INTO dbo.Donation (DonorId, ReceivedOn, Notes)
VALUES (@donorA, DATEADD(DAY, -14, CAST(SYSUTCDATETIME() AS date)), N'Dropped off at the depot');
SET @donationA = SCOPE_IDENTITY();
INSERT INTO dbo.Donation (DonorId, ReceivedOn, Notes)
VALUES (@donorB, DATEADD(DAY, -7, CAST(SYSUTCDATETIME() AS date)), N'Collected after the Sunday collection');
SET @donationB = SCOPE_IDENTITY();

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
-- Kind is 0 Stop, 1 Overnight, 2 Border, 3 Hub; Authority (a Border point only) is 0 UK, 1 EU, 2 UA. Each point keeps its
-- RoutePointId when the route is edited, which is what accommodation and progress marks will point at.
INSERT INTO dbo.ConvoyRouteStop (ConvoyId, Sequence, Name, Kind, Authority, City, Country, Postcode, CountryCode)
VALUES (@convoy, 1, N'Coventry depot',    3, NULL, N'Coventry', N'United Kingdom', N'CV1 0AA',  'GB'),
       (@convoy, 2, N'Dover port',        2, 0,    N'Dover',    N'United Kingdom', N'CT16 0AA', 'GB'),
       (@convoy, 3, N'Calais',            2, 1,    N'Calais',   N'France',         N'',         'FR'),
       (@convoy, 4, N'Poznan overnight',  1, NULL, N'Poznan',   N'Poland',         N'',         'PL'),
       (@convoy, 5, N'Lviv handover',     2, 2,    N'Lviv',     N'Ukraine',        N'',         'UA');

-- Vehicles. Transmission: 1 Manual, 2 Automatic. Fuel: 2 Diesel. InspectionStatus: 0 Pending,
-- 2 Passed — only a Passed vehicle may join a convoy.
-- ValueSource 2 is Purchased (the price paid) and 1 is Estimate (a figure for a donated vehicle); the Hilux has none yet.
INSERT INTO dbo.Vehicle (Vin, Plate, Brand, Model, Colour, Transmission, [Year], Fuel, WeightKg, InspectionStatus, ValueGbp, ValueSource)
VALUES ('SEEDVIN0000000001', N'AB12 CDE', N'Ford',       N'Transit',  N'White', 1, 2015, 2, 2000, 2, 4500.00, 2),
       ('SEEDVIN0000000002', N'FG34 HIJ', N'Volkswagen', N'Crafter',  N'Blue',  1, 2016, 2, 2100, 2, 3800.00, 1),
       ('SEEDVIN0000000003', N'KL56 MNO', N'Toyota',     N'Hilux',    N'Grey',  2, 2014, 2, 1900, 0, NULL,    NULL);

-- The truck list. The Hilux is left off it: it has not passed inspection, so it is the vehicle to
-- try assigning when you want to see the 409.
INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin, HandoverReceiverRef)
VALUES (@convoy, 'SEEDVIN0000000001', @aidHospital),
       (@convoy, 'SEEDVIN0000000002', NULL);

-- Crew: one seat per person per convoy. Role 0 is Driver.
INSERT INTO dbo.ConvoyVehicleCrew (ConvoyId, Vin, PersonId, [Role])
SELECT @convoy, 'SEEDVIN0000000001', p.Id, 0
FROM (SELECT TOP 2 Id FROM @people WHERE IsDriver = 1 ORDER BY LastName) AS p;

-- The first of those drivers leads the convoy: one open assignment, nominated by nobody in particular.
INSERT INTO dbo.ConvoyLeaderAssignment (ConvoyId, PersonId, [From])
SELECT TOP 1 @convoy, crew.PersonId, SYSUTCDATETIME()
FROM dbo.ConvoyVehicleCrew AS crew
WHERE crew.ConvoyId = @convoy
ORDER BY crew.PersonId;

-- Boxes waiting at the depots, not yet validated, each addressed to a registered receiver.
INSERT INTO dbo.Box (WeightKg, LocationId, ReceiverRef)
VALUES (12, @coventry, @aidHospital), (8, @coventry, @aidHospital), (15, @london, @reliefCharity);

-- Items name a category, which supplies the customs code unless the item has one of its own (the blankets do, to
-- show the item winning over its category). Value is pounds with its source, 0 Donor / 1 Estimate; ExpiresOn is a typed
-- date. PropertiesJson keeps only the open-ended rest, as strings -- a JSON number there would not read back as one.
INSERT INTO dbo.BoxItem (Id, BoxId, CategoryId, Description, Quantity, ValueGbp, ValueSource, ExpiresOn, CommodityCode, PropertiesJson)
SELECT NEWID(), b.Id, c.Id, i.Description, i.Quantity, i.ValueGbp, i.ValueSource, i.ExpiresOn, i.CommodityCode, i.PropertiesJson
FROM dbo.Box AS b
CROSS APPLY (VALUES (N'First aid kits',   N'Medical devices', 10, CAST(120.00 AS decimal(12,2)), 0, DATEADD(DAY, 400, CAST(SYSUTCDATETIME() AS date)), CAST(NULL AS varchar(10)), N'{"size":"small"}'),
                    (N'Thermal blankets', N'Other',           20, CAST(60.00 AS decimal(12,2)),  1, CAST(NULL AS date),                                    CAST('99190000' AS varchar(10)), N'{}')) AS i (Description, CategoryName, Quantity, ValueGbp, ValueSource, ExpiresOn, CommodityCode, PropertiesJson)
INNER JOIN dbo.ItemCategory AS c ON c.NameEn = i.CategoryName;

-- Every seeded item came in on a donation.
UPDATE dbo.BoxItem SET DonationId = @donationA WHERE Description = N'First aid kits';
UPDATE dbo.BoxItem SET DonationId = @donationB WHERE Description = N'Thermal blankets';

COMMIT;

PRINT 'Dev seed loaded.';
