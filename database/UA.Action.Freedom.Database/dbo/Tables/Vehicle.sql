/*
    A donated vehicle. VIN is the natural key.

    Column types are int (not tinyint/smallint) for the enum and year fields so they line up
    with the CLR types Dapper's constructor mapping expects for VehicleReadModel. VIN is
    varchar(32): pass it with SqlKey.Of(...), or an nvarchar parameter converts the column and
    scans the table.

    InspectionStatus is written only by PUT /vehicles/{vin}/inspection (the Mechanic's result)
    and gates convoy assignment: only Passed (2) may join a convoy. The CHECK keeps it inside the
    Domain InspectionStatus enum, because an out-of-range int maps to an enum value Dapper will
    happily construct and nothing downstream expects.

    HandedOverAt is stamped by POST /convoys/{id}/arrive for Delivered and Lost vehicles — they
    are part of the aid and stay in Ukraine — and such a vehicle is never offered for a convoy
    again. Write-once, absent from every UPDATE an ordinary edit issues.

    There is no ConvoyId column. Which convoy a vehicle is travelling with is dbo.ConvoyVehicle —
    the truck list — because a single mutable pointer could hold only the current convoy and was
    nulled at arrival, losing the list of an arrived convoy while its crew and insurance rows went
    on naming it. "Is this vehicle free?" is now: not handed over, and no ConvoyVehicle row that is
    un-withdrawn on a convoy that has not arrived.
*/
CREATE TABLE [dbo].[Vehicle] (
    [Vin]              varchar(32)    NOT NULL CONSTRAINT [PK_Vehicle] PRIMARY KEY,
    [Plate]            nvarchar(16)   NOT NULL,
    [Brand]            nvarchar(64)   NULL,
    [Model]            nvarchar(64)   NULL,
    [Colour]           nvarchar(32)   NULL,
    [Transmission]     int            NOT NULL CONSTRAINT [DF_Vehicle_Transmission] DEFAULT 0,
    [Notes]            nvarchar(1000) NULL,
    [Mileage]          int            NULL,
    [Servicing]        bit            NOT NULL CONSTRAINT [DF_Vehicle_Servicing] DEFAULT 0,
    [Year]             int            NOT NULL,
    [Fuel]             int            NOT NULL CONSTRAINT [DF_Vehicle_Fuel] DEFAULT 0,
    [PurchaserName]    nvarchar(200)  NULL,
    [PurchaseDate]     datetime2(0)   NULL,
    [WeightKg]         int            NOT NULL CONSTRAINT [DF_Vehicle_WeightKg] DEFAULT 0,

    -- Cargo capacity: optional. Decimal, not int, because these are measured to the nearest
    -- centimetre/hundredth of a kilogram, unlike the whole-number WeightKg/Mileage columns.
    [MaxCargoWeightKg] decimal(10,2)  NULL,
    [CargoWidthCm]     decimal(10,2)  NULL,
    [CargoDepthCm]     decimal(10,2)  NULL,
    [CargoHeightCm]    decimal(10,2)  NULL,

    [InspectionStatus] int            NOT NULL CONSTRAINT [DF_Vehicle_InspectionStatus] DEFAULT 0,
    [InspectionNotes]  nvarchar(2000) NULL,
    [HandedOverAt]     datetime2(0)   NULL,

    [CreatedAt]        datetime2(0)   NOT NULL CONSTRAINT [DF_Vehicle_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]        datetime2(0)   NOT NULL CONSTRAINT [DF_Vehicle_UpdatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [CK_Vehicle_InspectionStatus] CHECK ([InspectionStatus] >= 0 AND [InspectionStatus] <= 3)
);
GO
