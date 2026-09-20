/*
    THE TRUCK LIST. One row per vehicle per convoy, and the single statement of "this vehicle is
    travelling with this convoy".

    It used to be four statements that nothing reconciled: a mutable dbo.Vehicle.ConvoyId pointer,
    two independent nullable foreign keys on dbo.Manifest, and the leading columns of the crew and
    insurance tables. The pointer was the worst of them — POST /convoys/{id}/arrive nulled it for
    Returned vehicles, so an arrived convoy lost its own truck list while its crew and insurance
    rows went on naming a parent that no longer existed.

    Withdrawal is a stamp, not a delete. A vehicle that breaks down leaves the convoy and may be
    repaired and join a later one, or make its own way — but its manifest and its GMR still
    describe a real load, and which convoy it set off with is part of what happened. Deleting the
    row would take the crew and the insurance with it and orphan the manifest.

    Cascades: only ONE path may cascade into a child, so Vehicle cascades and Convoy does not.
    Deleting a donated vehicle takes its truck-list rows, and their crew and insurance, with it;
    deleting a convoy is NO ACTION and ConvoyRepository.DeleteAsync clears the children itself,
    in one transaction, exactly as it already did. A manifest still refuses both (see Manifest.sql).

    Vin is varchar(32): pass it with SqlKey.Of(...), or an nvarchar parameter converts the column
    and scans the table.
*/
CREATE TABLE [dbo].[ConvoyVehicle] (
    [ConvoyId]        int            NOT NULL,
    [Vin]             varchar(32)    NOT NULL,
    [AddedAt]         datetime2(0)   NOT NULL CONSTRAINT [DF_ConvoyVehicle_AddedAt] DEFAULT SYSUTCDATETIME(),

    -- Set when the vehicle left the convoy mid-journey. NULL means it is still travelling.
    [WithdrawnAt]     datetime2(0)   NULL,
    [WithdrawnReason] nvarchar(500)  NULL,

    CONSTRAINT [PK_ConvoyVehicle] PRIMARY KEY ([ConvoyId], [Vin]),
    CONSTRAINT [FK_ConvoyVehicle_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]),
    CONSTRAINT [FK_ConvoyVehicle_Vehicle] FOREIGN KEY ([Vin]) REFERENCES [dbo].[Vehicle] ([Vin]) ON DELETE CASCADE,

    -- A reason without a withdrawal would read as a vehicle that left for a stated cause and is
    -- somehow still on the road.
    CONSTRAINT [CK_ConvoyVehicle_Withdrawn] CHECK ([WithdrawnAt] IS NOT NULL OR [WithdrawnReason] IS NULL)
);
GO

-- "Which convoys has this vehicle been on?", and the lookup the Vehicle foreign key needs.
CREATE INDEX [IX_ConvoyVehicle_Vin] ON [dbo].[ConvoyVehicle] ([Vin]);
GO
