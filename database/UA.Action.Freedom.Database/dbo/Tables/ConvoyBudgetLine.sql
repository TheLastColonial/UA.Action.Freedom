/*
    A convoy's budget (O12, P3): at most one planned amount per cost type. Fuel, ferry, hotel,
    insurance and other are 0..4 (Domain.CostType), int rather than tinyint so the column lines up with
    the CLR enum Dapper hydrates. Replaced as a whole by PUT /convoys/{id}/budget, and not required to
    depart (O37): a convoy with no rows here simply has no budget.

    The CHECKs are written in SQL Server's normalised form (>= AND <=, never BETWEEN) or every publish
    recreates them and the Database workflow fails on a non-empty DeployReport.
*/
CREATE TABLE [dbo].[ConvoyBudgetLine] (
    [ConvoyId]      int              NOT NULL,
    [CostType]      int              NOT NULL,
    [PlannedGbp]    decimal(10,2)    NOT NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [PK_ConvoyBudgetLine] PRIMARY KEY ([ConvoyId], [CostType]),
    CONSTRAINT [FK_ConvoyBudgetLine_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyBudgetLine_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_ConvoyBudgetLine_CostType] CHECK ([CostType] >= 0 AND [CostType] <= 4),
    CONSTRAINT [CK_ConvoyBudgetLine_Planned] CHECK ([PlannedGbp] >= 0)
);
GO

-- Every Person delete checks this foreign key; unindexed it scans the table under lock.
CREATE INDEX [IX_ConvoyBudgetLine_LastChangedBy] ON [dbo].[ConvoyBudgetLine] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
