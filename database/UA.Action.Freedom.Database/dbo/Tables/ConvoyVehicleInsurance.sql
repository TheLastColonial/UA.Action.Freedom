/*
    Bought per vehicle per convoy by the Dispatcher, and it names the crew. So a crew change after
    it was recorded sets VoidedAt (in the same transaction as the crew write, in
    ConvoyVehicleRepository), and recording it again clears it. A manifest cannot depart unless its
    vehicle's insurance is recorded, not voided, and in cover on the day.

    Was dbo.VehicleInsurance. It now hangs off the truck-list entry rather than repeating
    (ConvoyId, Vin) as two unrelated foreign keys, so the row cannot describe a vehicle that is not
    on the convoy — and, unlike before, its parent still exists after the convoy has arrived.

    RecordedBySub is the token subject of whoever recorded it, never a request field. Cover dates
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
    [RecordedBySub] nvarchar(200) NOT NULL,
    [RecordedAt]    datetime2(0)  NOT NULL CONSTRAINT [DF_ConvoyVehicleInsurance_RecordedAt] DEFAULT SYSUTCDATETIME(),
    [VoidedAt]      datetime2(0)  NULL,

    CONSTRAINT [PK_ConvoyVehicleInsurance] PRIMARY KEY ([ConvoyId], [Vin]),
    CONSTRAINT [FK_ConvoyVehicleInsurance_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]) ON DELETE CASCADE,
    CONSTRAINT [CK_ConvoyVehicleInsurance_Cover] CHECK ([CoverEnd] >= [CoverStart]),
    CONSTRAINT [CK_ConvoyVehicleInsurance_Cost] CHECK ([CostGbp] IS NULL OR [CostGbp] >= 0)
);
