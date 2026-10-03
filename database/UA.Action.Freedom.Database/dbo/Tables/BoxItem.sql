/*
    An item packed in a box. What customs, the carrier and the value report depend on is a typed column;
    PropertiesJson keeps only the open-ended rest (size, condition, whatever a donation needs), as strings.

    CategoryId says what kind of thing this is (dbo.ItemCategory). CommodityCode is the item's own code,
    which wins when set; otherwise the filing sheet uses the category's code for the authority being filed
    with (ADR 0014). ValueGbp is pounds with no conversion and ValueSource says who gave the figure
    (Domain ValueSource: 0 Donor, 1 Estimate); both or neither. ExpiresOn is a date, not a property, because
    an already-expired item blocks its box from being validated (D1).
*/
CREATE TABLE [dbo].[BoxItem] (
    [Id]             uniqueidentifier NOT NULL CONSTRAINT [PK_BoxItem] PRIMARY KEY,
    [BoxId]          int              NOT NULL,
    [CategoryId]     int              NOT NULL,
    [Description]    nvarchar(400)    NOT NULL,
    -- The commodity code declared for this item on an ICS2 Entry Summary Declaration, which
    -- requires at least six digits per goods item. A column and not a key in PropertiesJson: the
    -- properties are open-ended precisely because nothing depends on them, and this is a
    -- customs-critical field that a border refusal turns on. 9919 00 00 covers humanitarian aid
    -- (EnsCommodity.HumanitarianAid); NULL means the item has no code of its own and the category's
    -- applies, and a missing one on both is reported on the filing sheet rather than assumed.
    [CommodityCode]  varchar(10)      NULL,
    [Quantity]       int              NULL,
    [ValueGbp]       decimal(12,2)    NULL,
    [ValueSource]    int              NULL,
    [ExpiresOn]      date             NULL,
    -- Which drop-off this item came from (ADR 0013). NULL for items entered before donations existed.
    [DonationId]     int              NULL,
    [PropertiesJson] nvarchar(max)    NOT NULL CONSTRAINT [DF_BoxItem_PropertiesJson] DEFAULT '{}',
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [CK_BoxItem_Quantity] CHECK ([Quantity] >= 1),
    CONSTRAINT [CK_BoxItem_ValueGbp] CHECK ([ValueGbp] >= 0),
    CONSTRAINT [CK_BoxItem_ValueSource] CHECK ([ValueSource] >= 0 AND [ValueSource] <= 1),
    CONSTRAINT [CK_BoxItem_ValueAndSource] CHECK (([ValueGbp] IS NULL AND [ValueSource] IS NULL) OR ([ValueGbp] IS NOT NULL AND [ValueSource] IS NOT NULL)),
    -- Items have no life outside their box: unpacking one is deleting the box.
    CONSTRAINT [FK_BoxItem_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_BoxItem_Category] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[ItemCategory] ([Id]),
    CONSTRAINT [FK_BoxItem_Donation] FOREIGN KEY ([DonationId]) REFERENCES [dbo].[Donation] ([Id]),
    CONSTRAINT [FK_BoxItem_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_BoxItem_BoxId] ON [dbo].[BoxItem] ([BoxId]);
GO

CREATE INDEX [IX_BoxItem_DonationId] ON [dbo].[BoxItem] ([DonationId]) WHERE [DonationId] IS NOT NULL;
GO
