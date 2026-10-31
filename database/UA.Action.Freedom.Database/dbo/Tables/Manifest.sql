/*
    The load sign-off for one vehicle on one convoy (ADR 0004), and its document pack: delivery notes
    and the border weight read through the vehicle's allocated boxes. The Id is a natural key like Vin — a document
    reference people read out at a border, not a surrogate. varchar(32): pass it with SqlKey.Of(...).

    (ConvoyId, Vin) is NOT NULL and is a composite foreign key to the truck list, with a unique
    constraint. That pair used to be two independent nullable foreign keys, so nothing checked the
    vehicle was on that convoy and nothing stopped one vehicle carrying two manifests — which
    mattered, because a sign-off that two rows could give is no sign-off at all. The manifest
    is now a child of the truck-list entry, which is what docs/process.puml has always described:
    Truck List Published, then Manifest Proposed.

    NO ACTION on the truck list, deliberately: a manifest is the record of what a vehicle carried
    across a border, so neither cancelling a convoy nor deleting a vehicle may erase it. Withdrawing
    a broken-down vehicle stamps ConvoyVehicle.WithdrawnAt and leaves this row entirely alone.

    Status is the three-state sign-off of docs/manifest-status.puml: 0 Proposed, 1 Approved, 2 Rejected,
    stored as int to line up with the CLR enum Dapper hydrates, in SQL Server's normalised CHECK form.
    The legal edges live in ManifestTransitions. Approved goes back to Proposed only when the load
    changes (X3): the repository that changes the load does it in the same transaction and stamps
    ReopenedAt, which the next approval clears. Nothing is frozen: a filed declaration is compared
    against its own snapshot of the load, not against this row.
*/
CREATE TABLE [dbo].[Manifest] (
    [Id]                   varchar(32)    NOT NULL CONSTRAINT [PK_Manifest] PRIMARY KEY,
    [ConvoyId]             int            NOT NULL,
    [Vin]                  varchar(32)    NOT NULL,
    [Status]               int            NOT NULL CONSTRAINT [DF_Manifest_Status] DEFAULT 0,
    [DeliveryNotes]        nvarchar(2000) NULL,
    [ReopenedAt]           datetime2(0)   NULL,
    [CreatedAt]            datetime2(0)   NOT NULL CONSTRAINT [DF_Manifest_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]            datetime2(0)   NOT NULL CONSTRAINT [DF_Manifest_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [FK_Manifest_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]),

    -- One manifest per vehicle per convoy.
    CONSTRAINT [UQ_Manifest_ConvoyVehicle] UNIQUE ([ConvoyId], [Vin]),
    CONSTRAINT [CK_Manifest_Status] CHECK ([Status] >= 0 AND [Status] <= 2),
    CONSTRAINT [FK_Manifest_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

-- "Which manifests are on convoy 7?" was a table scan: the only index was the clustered key on Id.
GO

CREATE INDEX [IX_Manifest_ConvoyId] ON [dbo].[Manifest] ([ConvoyId]);
GO

CREATE INDEX [IX_Manifest_LastChangedBy] ON [dbo].[Manifest] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
