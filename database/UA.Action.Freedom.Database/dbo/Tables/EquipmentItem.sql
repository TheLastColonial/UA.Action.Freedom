/*
    The catalogue of equipment the charity buys for a vehicle, such as warning triangles (O13). It is
    accounted for separately from donations: no donor, and never part of the value delivered. UnitCostGbp
    is a default that prices a line when its own cost is not recorded; it is optional.

    Names are unique so the same item is not catalogued twice.
*/
CREATE TABLE [dbo].[EquipmentItem] (
    [Id]            int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_EquipmentItem] PRIMARY KEY,
    [Name]          nvarchar(200)    NOT NULL,
    [UnitCostGbp]   decimal(10,2)    NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [UQ_EquipmentItem_Name] UNIQUE ([Name]),
    CONSTRAINT [FK_EquipmentItem_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_EquipmentItem_UnitCost] CHECK ([UnitCostGbp] IS NULL OR [UnitCostGbp] >= 0)
);
GO

CREATE INDEX [IX_EquipmentItem_LastChangedBy] ON [dbo].[EquipmentItem] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
