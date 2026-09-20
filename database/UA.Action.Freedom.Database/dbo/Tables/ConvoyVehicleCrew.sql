/*
    The people travelling in one vehicle, on one convoy, on one leg of the journey. Was
    dbo.VehicleDriver, and it is now the ONLY crew record in the system — dbo.ManifestDriverTeam
    is gone.

    There used to be two. This table decided the insurance (which names the crew, and is voided by
    any change to it) and therefore whether a manifest could depart; dbo.ManifestDriverTeam held a
    primary/secondary pair per leg and had no effect on anything. Nothing linked them, so a manifest
    could name a UK driver who was not on the vehicle at all, and the document said one thing while
    the insurance covered another.

      * Leg is JourneyLeg: 0 is UK to Europe, 1 is Europe to Ukraine. Crew is per leg because a
        handover at the European border is a real event — one crew takes the vehicle out of the UK,
        another takes it into Ukraine.
      * ONE SEAT PER PERSON PER LEG: UQ_ConvoyVehicleCrew_Convoy_Person_Leg. A person cannot be in
        two vehicles on the same leg of the same journey; they may legitimately change vehicle at
        the border.
      * Role is CrewRole: a Driver must be a volunteer registered to drive, a Passenger can be any
        volunteer. A vehicle is ready for a leg with two drivers on it; passengers do not count.
        There is no primary/secondary distinction — nothing used it, and readiness counts drivers.
      * The key names the convoy through ConvoyVehicle, so the crew of an earlier journey stays as
        history when a Returned vehicle joins a later convoy.
*/
CREATE TABLE [dbo].[ConvoyVehicleCrew] (
    [ConvoyId]  int              NOT NULL,
    [Vin]       varchar(32)      NOT NULL,
    [PersonId]  uniqueidentifier NOT NULL,
    [Leg]       int              NOT NULL,
    [Role]      int              NOT NULL CONSTRAINT [DF_ConvoyVehicleCrew_Role] DEFAULT 0,
    [CreatedAt] datetime2(0)     NOT NULL CONSTRAINT [DF_ConvoyVehicleCrew_CreatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_ConvoyVehicleCrew] PRIMARY KEY ([ConvoyId], [Vin], [PersonId], [Leg]),

    -- Cascades from the truck-list entry, which itself cascades from Vehicle. That is the one
    -- permitted path: FK_ConvoyVehicle_Convoy is NO ACTION precisely so this one may cascade.
    CONSTRAINT [FK_ConvoyVehicleCrew_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyVehicleCrew_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id]),

    CONSTRAINT [CK_ConvoyVehicleCrew_Role] CHECK ([Role] = 0 OR [Role] = 1),
    CONSTRAINT [CK_ConvoyVehicleCrew_Leg] CHECK ([Leg] = 0 OR [Leg] = 1),
    CONSTRAINT [UQ_ConvoyVehicleCrew_Convoy_Person_Leg] UNIQUE ([ConvoyId], [PersonId], [Leg])
);
GO

-- "Is this volunteer on a live crew?" — the erasure check in PersonRepository reads this way.
CREATE INDEX [IX_ConvoyVehicleCrew_PersonId] ON [dbo].[ConvoyVehicleCrew] ([PersonId]);
GO
