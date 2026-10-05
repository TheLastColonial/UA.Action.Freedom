/*
    Which locations a Loader manages, and which they managed before (O14, O31, ADR 0010). A Loader sees only the
    locations with an open row here. Removing an assignment closes it (Until), so who managed a hub when is kept. The
    filtered unique index is the rule "assigned once at a time": at most one open row per person and location. Only an
    Administrator writes it.
*/
CREATE TABLE [dbo].[LoaderLocationAssignment] (
    [Id]         int              IDENTITY(1,1) NOT NULL,
    [LocationId] int              NOT NULL,
    [PersonId]   uniqueidentifier NOT NULL,
    [From]       datetime2(0)     NOT NULL,
    [Until]      datetime2(0)     NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [PK_LoaderLocationAssignment] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LoaderLocationAssignment_Location] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[Location] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_LoaderLocationAssignment_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [FK_LoaderLocationAssignment_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE UNIQUE INDEX [UX_LoaderLocationAssignment_OpenPerPersonAndLocation]
    ON [dbo].[LoaderLocationAssignment] ([PersonId], [LocationId]) WHERE [Until] IS NULL;
GO

CREATE INDEX [IX_LoaderLocationAssignment_LocationId] ON [dbo].[LoaderLocationAssignment] ([LocationId]);
GO

-- Every erasure asks whether a record names the person, and an unindexed foreign key scans under lock.
CREATE INDEX [IX_LoaderLocationAssignment_LastChangedBy] ON [dbo].[LoaderLocationAssignment] ([LastChangedBy]);
GO
