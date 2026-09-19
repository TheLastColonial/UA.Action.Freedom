-- One team per leg: 0 is UK to Europe, 1 is Europe to Ukraine. The pair is the primary key, so
-- assigning a team to a leg replaces whoever was on it rather than accumulating crews.
CREATE TABLE [dbo].[ManifestDriverTeam] (
    [ManifestId]        varchar(32)      NOT NULL,
    [Leg]               int              NOT NULL,
    [PrimaryPersonId]   uniqueidentifier NOT NULL,
    [SecondaryPersonId] uniqueidentifier NULL,

    CONSTRAINT [PK_ManifestDriverTeam] PRIMARY KEY ([ManifestId], [Leg]),
    CONSTRAINT [FK_ManifestDriverTeam_Manifest] FOREIGN KEY ([ManifestId]) REFERENCES [dbo].[Manifest] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ManifestDriverTeam_Primary] FOREIGN KEY ([PrimaryPersonId]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [FK_ManifestDriverTeam_Secondary] FOREIGN KEY ([SecondaryPersonId]) REFERENCES [dbo].[Person] ([Id]),

    -- A pair is two people. The same volunteer twice would read as crewed while leaving somebody
    -- driving a leg to Ukraine alone.
    CONSTRAINT [CK_ManifestDriverTeam_DistinctDrivers] CHECK (
        [SecondaryPersonId] IS NULL OR [SecondaryPersonId] <> [PrimaryPersonId])
);
