/*
    A customs declaration about one vehicle's load (ADR 0005, X6): GMR, ENS, ELO or a Ukrainian goods
    list, all on one lifecycle. Hangs off the truck-list entry like the crew and the insurance.
    ReceiverRef is set for a goods list only (one per receiver per vehicle per convoy).
    Reference is write-once in application terms: it is stamped by the transition that files the
    declaration and never by an ordinary update. ReasonCode is a bounded code, never the authority's
    free text, because that can quote the declaration it objected to.
    Kind: 0 GMR, 1 ENS, 2 ELO, 3 goods list. Status: 0 draft .. 7 closed (Domain.DeclarationStatus).
    At most one declaration that is not withdrawn per scope and kind; a withdrawn one is kept as history.
    SnapshotJson is the load this declaration was written from (Domain.LoadSnapshot), stored when it becomes
    ready to file or, for one recorded straight to filed, when it is recorded. It is compared with the load as
    it is now, and a difference is what makes the declaration stale (ADR 0005): staleness is derived on read and
    Stale is never written by a read. SnapshotVersion says which fields the JSON has, so a snapshot written by
    an older version is compared only on those. Both or neither; no names, addresses or contacts are in it.
*/
CREATE TABLE [dbo].[Declaration] (
    [Id]            int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Declaration] PRIMARY KEY,
    [ConvoyId]      int              NOT NULL,
    [Vin]           varchar(32)      NOT NULL,
    [ReceiverRef]   uniqueidentifier NULL,
    [Kind]          int              NOT NULL,
    [Status]        int              NOT NULL,
    [Reference]     nvarchar(100)    NULL,
    [ReasonCode]    varchar(32)      NULL,
    [RecordedBy]    uniqueidentifier NULL,
    [RecordedAt]    datetime2(0)     NULL,
    [SnapshotJson]    nvarchar(max)  NULL,
    [SnapshotVersion] int            NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [FK_Declaration_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]),
    CONSTRAINT [FK_Declaration_Receiver] FOREIGN KEY ([ReceiverRef]) REFERENCES [dbo].[Receiver] ([ReceiverRef]),
    CONSTRAINT [FK_Declaration_RecordedBy] FOREIGN KEY ([RecordedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [FK_Declaration_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_Declaration_Kind] CHECK ([Kind] >= 0 AND [Kind] <= 3),
    CONSTRAINT [CK_Declaration_Status] CHECK ([Status] >= 0 AND [Status] <= 7),
    CONSTRAINT [CK_Declaration_Snapshot] CHECK (
        ([SnapshotJson] IS NULL AND [SnapshotVersion] IS NULL)
        OR ([SnapshotJson] IS NOT NULL AND [SnapshotVersion] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_Declaration_Current] ON [dbo].[Declaration] ([ConvoyId], [Vin], [Kind])
    WHERE [ReceiverRef] IS NULL AND [Status] <> 6;
GO

CREATE UNIQUE INDEX [UX_Declaration_CurrentPerReceiver] ON [dbo].[Declaration] ([ConvoyId], [Vin], [Kind], [ReceiverRef])
    WHERE [ReceiverRef] IS NOT NULL AND [Status] <> 6;
GO

-- The two foreign keys to dbo.Person are checked by every Person delete; without an index each check
-- scans this table under lock, which is how an erasure deadlocks against a declaration being filed.
CREATE INDEX [IX_Declaration_RecordedBy] ON [dbo].[Declaration] ([RecordedBy]) WHERE [RecordedBy] IS NOT NULL;
GO

CREATE INDEX [IX_Declaration_LastChangedBy] ON [dbo].[Declaration] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
