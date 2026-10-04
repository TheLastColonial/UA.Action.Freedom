/*
    A vehicle's outbound ferry booking (P1). Per vehicle, because each lorry has its own ticket,
    reference and sailing; outbound only, because vehicles are handed over in Ukraine and not driven
    back. This replaces the Manifest.FerryBookingComplete flag, which said only that somebody had
    ticked a box. CostGbp is optional and feeds the convoy budget (plan 12).

    Hangs off the truck-list entry like the crew and the insurance: removing the entry before
    publication takes the booking with it, and withdrawal keeps it.
*/
CREATE TABLE [dbo].[ConvoyVehicleFerryBooking] (
    [ConvoyId]      int            NOT NULL,
    [Vin]           varchar(32)    NOT NULL,
    [Operator]      nvarchar(200)  NOT NULL,
    [Reference]     nvarchar(100)  NOT NULL,
    [SailingAt]     datetime2(0)   NOT NULL,
    [TicketDetails] nvarchar(1000) NULL,
    [CostGbp]       decimal(10,2)  NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [PK_ConvoyVehicleFerryBooking] PRIMARY KEY ([ConvoyId], [Vin]),
    CONSTRAINT [FK_ConvoyVehicleFerryBooking_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyVehicleFerryBooking_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_ConvoyVehicleFerryBooking_Cost] CHECK ([CostGbp] IS NULL OR [CostGbp] >= 0)
);
GO
