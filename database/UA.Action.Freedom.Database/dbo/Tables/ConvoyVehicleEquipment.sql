/*
    Equipment the charity has bought for one vehicle on one convoy (O13). Hangs off the truck-list entry
    like the crew, the insurance and the ferry booking: removing the entry before publication takes the
    equipment with it, and withdrawal keeps it.

    CostGbp is optional: when it is null the line costs Quantity x the catalogue's UnitCostGbp. Either
    way it counts under the Other line of the convoy's budget, and never towards the value delivered.
*/
CREATE TABLE [dbo].[ConvoyVehicleEquipment] (
    [ConvoyId]        int              NOT NULL,
    [Vin]             varchar(32)      NOT NULL,
    [EquipmentItemId] int              NOT NULL,
    [Quantity]        int              NOT NULL,
    [CostGbp]         decimal(10,2)    NULL,
    [LastChangedBy]   uniqueidentifier NULL,
    [LastChangedAt]   datetime2(0)     NULL,

    CONSTRAINT [PK_ConvoyVehicleEquipment] PRIMARY KEY ([ConvoyId], [Vin], [EquipmentItemId]),
    CONSTRAINT [FK_ConvoyVehicleEquipment_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyVehicleEquipment_EquipmentItem] FOREIGN KEY ([EquipmentItemId]) REFERENCES [dbo].[EquipmentItem] ([Id]),
    CONSTRAINT [FK_ConvoyVehicleEquipment_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_ConvoyVehicleEquipment_Quantity] CHECK ([Quantity] >= 1),
    CONSTRAINT [CK_ConvoyVehicleEquipment_Cost] CHECK ([CostGbp] IS NULL OR [CostGbp] >= 0)
);
GO

CREATE INDEX [IX_ConvoyVehicleEquipment_EquipmentItemId] ON [dbo].[ConvoyVehicleEquipment] ([EquipmentItemId]);
GO

CREATE INDEX [IX_ConvoyVehicleEquipment_LastChangedBy] ON [dbo].[ConvoyVehicleEquipment] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
