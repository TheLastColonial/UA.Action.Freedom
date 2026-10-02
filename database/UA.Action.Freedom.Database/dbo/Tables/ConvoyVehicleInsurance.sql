/*
    Bought per vehicle per convoy by the Dispatcher, and it names the drivers it covers
    (dbo.ConvoyVehicleInsuranceDriver). Removing a driver does not void it; a driver added after it
    was recorded is uncovered until it is recorded again. VoidedAt is an explicit void and no crew
    change sets it. A manifest cannot depart unless its vehicle's insurance is recorded, not voided,
    in cover on the day and names every driver.

    Was dbo.VehicleInsurance. It now hangs off the truck-list entry rather than repeating
    (ConvoyId, Vin) as two unrelated foreign keys, so the row cannot describe a vehicle that is not
    on the convoy — and, unlike before, its parent still exists after the convoy has arrived.

    RecordedByPersonId is the volunteer who recorded it, resolved from their login and never a
    request field. It is a foreign key to the anonymous identity, so erasing the volunteer keeps the
    row and it reads "Former volunteer". Cover dates
    are `date`; cost is optional.
*/
CREATE TABLE [dbo].[ConvoyVehicleInsurance] (
    [ConvoyId]      int           NOT NULL,
    [Vin]           varchar(32)   NOT NULL,
    [Insurer]       nvarchar(200) NOT NULL,
    [PolicyNumber]  nvarchar(100) NOT NULL,
    [CoverStart]    date          NOT NULL,
    [CoverEnd]      date          NOT NULL,
    [CostGbp]       decimal(10,2) NULL,
    [RecordedByPersonId] uniqueidentifier NOT NULL,
    [RecordedAt]    datetime2(0)  NOT NULL CONSTRAINT [DF_ConvoyVehicleInsurance_RecordedAt] DEFAULT SYSUTCDATETIME(),
    [VoidedAt]      datetime2(0)  NULL,

    CONSTRAINT [PK_ConvoyVehicleInsurance] PRIMARY KEY ([ConvoyId], [Vin]),
    CONSTRAINT [FK_ConvoyVehicleInsurance_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyVehicleInsurance_Person] FOREIGN KEY ([RecordedByPersonId]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_ConvoyVehicleInsurance_Cover] CHECK ([CoverEnd] >= [CoverStart]),
    CONSTRAINT [CK_ConvoyVehicleInsurance_Cost] CHECK ([CostGbp] IS NULL OR [CostGbp] >= 0)
);
