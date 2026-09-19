/*
    The central document of the system: one vehicle, on one convoy, with its two driver teams
    and its cargo. The Id is a natural key like Vin — a document reference people read out at a
    border, not a surrogate. varchar(32): pass it with SqlKey.Of(...).

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
    [Vin]                  varchar(32)    NULL,
    [ConvoyId]             int            NULL,
    [Status]               int            NOT NULL CONSTRAINT [DF_Manifest_Status] DEFAULT 0,
    [DeliveryNotes]        nvarchar(2000) NULL,
    [FerryBookingComplete] bit            NOT NULL CONSTRAINT [DF_Manifest_Ferry] DEFAULT 0,
    [GmrSubmittedAt]       datetime2(0)   NULL,
    [CreatedAt]            datetime2(0)   NOT NULL CONSTRAINT [DF_Manifest_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]            datetime2(0)   NOT NULL CONSTRAINT [DF_Manifest_UpdatedAt] DEFAULT SYSUTCDATETIME(),

    -- A vehicle or convoy cannot be deleted out from under a manifest that names it.
    CONSTRAINT [FK_Manifest_Vehicle] FOREIGN KEY ([Vin]) REFERENCES [dbo].[Vehicle] ([Vin]),
    CONSTRAINT [FK_Manifest_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id])
);
