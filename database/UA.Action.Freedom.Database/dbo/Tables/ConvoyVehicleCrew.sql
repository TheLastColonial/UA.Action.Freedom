/*
    The people travelling in one vehicle, on one convoy. Was
    dbo.VehicleDriver, and it is now the ONLY crew record in the system — dbo.ManifestDriverTeam
    is gone.

    There used to be two. This table decided the insurance (which names the drivers) and therefore whether a manifest could depart; dbo.ManifestDriverTeam held a
    primary/secondary pair and had no effect on anything. Nothing linked them, so a manifest
    could name a UK driver who was not on the vehicle at all, and the document said one thing while
    the insurance covered another.

      * ONE SEAT PER PERSON PER CONVOY: UQ_ConvoyVehicleCrew_Convoy_Person. A person cannot be in
        two vehicles on the same convoy. There are no journey legs: a seat is one person on one
        vehicle on one convoy.
      * Role is CrewRole: a Driver must be a volunteer registered to drive, a Passenger can be any
        volunteer. A vehicle needs one driver and is advised two; passengers do not count.
        There is no primary/secondary distinction — nothing used it, and readiness counts drivers.
      * The key names the convoy through ConvoyVehicle, so the crew of an earlier journey stays as
        history when a Returned vehicle joins a later convoy.
*/
CREATE TABLE [dbo].[ConvoyVehicleCrew] (
    [ConvoyId]  int              NOT NULL,
    [Vin]       varchar(32)      NOT NULL,
    [PersonId]  uniqueidentifier NOT NULL,
    [Role]      int              NOT NULL CONSTRAINT [DF_ConvoyVehicleCrew_Role] DEFAULT 0,
    [CreatedAt] datetime2(0)     NOT NULL CONSTRAINT [DF_ConvoyVehicleCrew_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [PK_ConvoyVehicleCrew] PRIMARY KEY ([ConvoyId], [Vin], [PersonId]),

    -- Cascades from the truck-list entry, which itself cascades from Vehicle. That is the one
    -- permitted path: FK_ConvoyVehicle_Convoy is NO ACTION precisely so this one may cascade.
    CONSTRAINT [FK_ConvoyVehicleCrew_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyVehicleCrew_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id]),

    CONSTRAINT [CK_ConvoyVehicleCrew_Role] CHECK ([Role] = 0 OR [Role] = 1),
    CONSTRAINT [UQ_ConvoyVehicleCrew_Convoy_Person] UNIQUE ([ConvoyId], [PersonId]),
    CONSTRAINT [FK_ConvoyVehicleCrew_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

-- "Is this volunteer on a live crew?" — the erasure check in PersonRepository reads this way.
CREATE INDEX [IX_ConvoyVehicleCrew_PersonId] ON [dbo].[ConvoyVehicleCrew] ([PersonId]);
GO
