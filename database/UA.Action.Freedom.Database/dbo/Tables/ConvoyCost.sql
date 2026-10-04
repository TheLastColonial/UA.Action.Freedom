/*
    A cost somebody entered against a convoy (O12, P3). Only fuel (0) and other (4) are stored here: a
    ferry, hotel or insurance cost lives on its booking or policy and is read from it, so it is never
    entered twice. Until the Convoy Leader's checklist exists the Dispatcher enters fuel (plan 18).

    A cost is added or deleted and never edited, so LastChangedBy is also who entered it.

    Vin is optional and is deliberately not a foreign key to the truck-list entry: a cost is money
    already spent, and it must survive the vehicle being taken off the list. The API checks the vehicle
    is on the convoy when the cost is entered.

    No payee or bank fields: reimbursing volunteers is out of scope (O10).
*/
CREATE TABLE [dbo].[ConvoyCost] (
    [Id]            int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ConvoyCost] PRIMARY KEY,
    [ConvoyId]      int              NOT NULL,
    [CostType]      int              NOT NULL,
    [Vin]           varchar(32)      NULL,
    [AmountGbp]     decimal(10,2)    NOT NULL,
    [Note]          nvarchar(500)    NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [FK_ConvoyCost_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyCost_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_ConvoyCost_CostType] CHECK ([CostType] = 0 OR [CostType] = 4),
    CONSTRAINT [CK_ConvoyCost_Amount] CHECK ([AmountGbp] >= 0)
);
GO

CREATE INDEX [IX_ConvoyCost_ConvoyId] ON [dbo].[ConvoyCost] ([ConvoyId]);
GO

CREATE INDEX [IX_ConvoyCost_LastChangedBy] ON [dbo].[ConvoyCost] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
