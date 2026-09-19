-- Cargo. A box travels on at most one manifest, which the primary key on BoxId enforces: the
-- same box on two manifests would be counted twice at a border and arrive once.
CREATE TABLE [dbo].[ManifestBox] (
    [BoxId]      int         NOT NULL CONSTRAINT [PK_ManifestBox] PRIMARY KEY,
    [ManifestId] varchar(32) NOT NULL,

    CONSTRAINT [FK_ManifestBox_Manifest] FOREIGN KEY ([ManifestId]) REFERENCES [dbo].[Manifest] ([Id]) ON DELETE CASCADE,
    -- Removing a box from the system takes it off the manifest with it.
    CONSTRAINT [FK_ManifestBox_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_ManifestBox_ManifestId] ON [dbo].[ManifestBox] ([ManifestId]);
GO
