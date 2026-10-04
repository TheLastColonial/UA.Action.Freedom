/*
    Who leads a convoy, and who led it before (D8, D17, P14). A reassignment closes the open row (Until) and opens
    another, so the history of who led and when is kept. The filtered unique index is the rule "exactly one leader":
    at most one row per convoy has no Until. The person must be a Driver crewed on the convoy; that is checked in
    the same transaction as the nomination, not here, because it spans the crew table.
*/
CREATE TABLE [dbo].[ConvoyLeaderAssignment] (
    [Id]       int              IDENTITY(1,1) NOT NULL,
    [ConvoyId] int              NOT NULL,
    [PersonId] uniqueidentifier NOT NULL,
    [From]     datetime2(0)     NOT NULL,
    [Until]    datetime2(0)     NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [PK_ConvoyLeaderAssignment] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ConvoyLeaderAssignment_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyLeaderAssignment_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [FK_ConvoyLeaderAssignment_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE UNIQUE INDEX [UX_ConvoyLeaderAssignment_OpenPerConvoy] ON [dbo].[ConvoyLeaderAssignment] ([ConvoyId]) WHERE [Until] IS NULL;
GO

-- Every erasure asks whether a record names the person, and an unindexed foreign key scans under lock.
CREATE INDEX [IX_ConvoyLeaderAssignment_PersonId] ON [dbo].[ConvoyLeaderAssignment] ([PersonId]);
GO
