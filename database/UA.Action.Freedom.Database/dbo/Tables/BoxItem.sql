/*
    An item packed in a box. Item properties are open-ended (size, condition, expiry, whatever a
    donation needs), so they are stored as a JSON document rather than as a table nobody could
    keep up with.
*/
CREATE TABLE [dbo].[BoxItem] (
    [Id]             uniqueidentifier NOT NULL CONSTRAINT [PK_BoxItem] PRIMARY KEY,
    [BoxId]          int              NOT NULL,
    [Description]    nvarchar(400)    NOT NULL,
    -- The commodity code declared for this item on an ICS2 Entry Summary Declaration, which
    -- requires at least six digits per goods item. A column and not a key in PropertiesJson: the
    -- properties are open-ended precisely because nothing depends on them, and this is a
    -- customs-critical field that a border refusal turns on. 9919 00 00 covers humanitarian aid
    -- (EnsCommodity.HumanitarianAid); NULL means nobody has classified the item yet, and the
    -- filing sheet reports it as missing rather than assuming.
    [CommodityCode]  varchar(10)      NULL,
    [PropertiesJson] nvarchar(max)    NOT NULL CONSTRAINT [DF_BoxItem_PropertiesJson] DEFAULT '{}',
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    -- Items have no life outside their box: unpacking one is deleting the box.
    CONSTRAINT [FK_BoxItem_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_BoxItem_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_BoxItem_BoxId] ON [dbo].[BoxItem] ([BoxId]);
GO
