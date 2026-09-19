/*
    An item packed in a box. Item properties are open-ended (size, condition, expiry, whatever a
    donation needs), so they are stored as a JSON document rather than as a table nobody could
    keep up with.
*/
CREATE TABLE [dbo].[BoxItem] (
    [Id]             uniqueidentifier NOT NULL CONSTRAINT [PK_BoxItem] PRIMARY KEY,
    [BoxId]          int              NOT NULL,
    [Description]    nvarchar(400)    NOT NULL,
    [PropertiesJson] nvarchar(max)    NOT NULL CONSTRAINT [DF_BoxItem_PropertiesJson] DEFAULT '{}',

    -- Items have no life outside their box: unpacking one is deleting the box.
    CONSTRAINT [FK_BoxItem_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_BoxItem_BoxId] ON [dbo].[BoxItem] ([BoxId]);
GO
