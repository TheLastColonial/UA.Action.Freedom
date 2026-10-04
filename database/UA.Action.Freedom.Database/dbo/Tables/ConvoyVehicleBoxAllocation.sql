/*
    CARGO. A box allocated to one truck-list entry (ADR 0004). The primary key on BoxId is the rule
    that a box is on at most one vehicle: the same box on two would be counted twice at a border and
    arrive once, and moving it is a single UPDATE of the key's other columns.

    Cascades: from the truck-list entry (removing an entry before publication takes its cargo with
    it, as it takes its crew) and from the Box (removing a box takes it off its vehicle). These are
    two different roots, so there is no multiple-cascade-path problem. The convoy itself is NO
    ACTION, as it is for dbo.ConvoyVehicle.

    Vin is varchar(32): pass it with SqlKey.Of(...).
*/
CREATE TABLE [dbo].[ConvoyVehicleBoxAllocation] (
    [BoxId]         int          NOT NULL CONSTRAINT [PK_ConvoyVehicleBoxAllocation] PRIMARY KEY,
    [ConvoyId]      int          NOT NULL,
    [Vin]           varchar(32)  NOT NULL,
    [AllocatedAt]   datetime2(0) NOT NULL CONSTRAINT [DF_ConvoyVehicleBoxAllocation_AllocatedAt] DEFAULT SYSUTCDATETIME(),
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [FK_ConvoyVehicleBoxAllocation_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyVehicleBoxAllocation_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin]) REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyVehicleBoxAllocation_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_ConvoyVehicleBoxAllocation_ConvoyVehicle] ON [dbo].[ConvoyVehicleBoxAllocation] ([ConvoyId], [Vin]);
GO
