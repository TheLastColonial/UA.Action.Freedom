/*
    The document pack for one vehicle on one convoy: its cargo, its border weight, its Goods
    Movement Reference and its ferry booking. The Id is a natural key like Vin — a document
    reference people read out at a border, not a surrogate. varchar(32): pass it with SqlKey.Of(...).

    (ConvoyId, Vin) is NOT NULL and is a composite foreign key to the truck list, with a unique
    constraint. That pair used to be two independent nullable foreign keys, so nothing checked the
    vehicle was on that convoy and nothing stopped one vehicle carrying two manifests — which
    mattered, because ConvoyRepository.ArriveAsync asks "does this vehicle have a finished
    manifest?" and would have been satisfied by whichever of the two finished first. The manifest
    is now a child of the truck-list entry, which is what docs/process.puml has always described:
    Truck List Published, then Manifest Proposed.

    NO ACTION on the truck list, deliberately: a manifest is the record of what a vehicle carried
    across a border, so neither cancelling a convoy nor deleting a vehicle may erase it. Withdrawing
    a broken-down vehicle stamps ConvoyVehicle.WithdrawnAt and leaves this row entirely alone.

    Status is the ten-state model of docs/manifest-status.puml, stored as int to line up with the
    CLR enum Dapper hydrates. The legal edges live in ManifestTransitions, not here: a CHECK
    constraint would have to be kept in step with the code by hand, and the code is where the two
    rules the diagram cannot express already live.

    GmrSubmittedAt is the freeze. recommendations 5.2 records the ruling that once a GMR is
    created no edit may modify the manifest; the application refuses every write once this is
    set, and only the progress transitions remain reachable.
*/
CREATE TABLE [dbo].[Manifest] (
    [Id]                   varchar(32)    NOT NULL CONSTRAINT [PK_Manifest] PRIMARY KEY,
    [ConvoyId]             int            NOT NULL,
    [Vin]                  varchar(32)    NOT NULL,
    [Status]               int            NOT NULL CONSTRAINT [DF_Manifest_Status] DEFAULT 0,
    [DeliveryNotes]        nvarchar(2000) NULL,
    [FerryBookingComplete] bit            NOT NULL CONSTRAINT [DF_Manifest_Ferry] DEFAULT 0,
    [GmrSubmittedAt]       datetime2(0)   NULL,
    [CreatedAt]            datetime2(0)   NOT NULL CONSTRAINT [DF_Manifest_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]            datetime2(0)   NOT NULL CONSTRAINT [DF_Manifest_UpdatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [FK_Manifest_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]),

    -- One manifest per vehicle per convoy. Arrival asks each vehicle for its finished manifest and
    -- has to get one answer.
    CONSTRAINT [UQ_Manifest_ConvoyVehicle] UNIQUE ([ConvoyId], [Vin])
);
GO

-- "Which manifests are on convoy 7?" was a table scan: the only index was the clustered key on Id.
CREATE INDEX [IX_Manifest_ConvoyId] ON [dbo].[Manifest] ([ConvoyId]);
GO
