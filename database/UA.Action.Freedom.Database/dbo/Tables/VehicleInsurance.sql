/*
    Bought per vehicle per convoy by the Dispatcher, and it names the crew. So a crew change
    after it was recorded sets VoidedAt (in the same transaction as the crew write, in
    ConvoyRepository), and recording it again clears it. A manifest cannot depart unless its
    vehicle's insurance is recorded, not voided, and in cover on the day.

    RecordedBySub is the token subject of whoever recorded it, never a request field. Cover dates
    are `date`; cost is optional.

    FK_VehicleInsurance_Convoy is NO ACTION for the reason given on dbo.VehicleDriver: a second
    cascade path from Convoy is refused. ConvoyRepository clears it when a vehicle leaves a
    convoy or a convoy is cancelled.
*/
CREATE TABLE [dbo].[VehicleInsurance] (
    [ConvoyId]      int           NOT NULL,
    [Vin]           varchar(32)   NOT NULL,
    [Insurer]       nvarchar(200) NOT NULL,
    [PolicyNumber]  nvarchar(100) NOT NULL,
    [CoverStart]    date          NOT NULL,
    [CoverEnd]      date          NOT NULL,
    [CostGbp]       decimal(10,2) NULL,
    [RecordedBySub] nvarchar(200) NOT NULL,
    [RecordedAt]    datetime2(0)  NOT NULL CONSTRAINT [DF_VehicleInsurance_RecordedAt] DEFAULT SYSUTCDATETIME(),
    [VoidedAt]      datetime2(0)  NULL,

    CONSTRAINT [PK_VehicleInsurance] PRIMARY KEY ([ConvoyId], [Vin]),
    CONSTRAINT [FK_VehicleInsurance_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]),
    CONSTRAINT [FK_VehicleInsurance_Vehicle] FOREIGN KEY ([Vin]) REFERENCES [dbo].[Vehicle] ([Vin]) ON DELETE CASCADE,
    CONSTRAINT [CK_VehicleInsurance_Cover] CHECK ([CoverEnd] >= [CoverStart]),
    CONSTRAINT [CK_VehicleInsurance_Cost] CHECK ([CostGbp] IS NULL OR [CostGbp] >= 0)
);
