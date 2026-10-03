/*
    The customs code a category is declared under, per authority: 0 UK, 1 EU, 2 UA (Domain
    CustomsAuthority). One row per authority at most. An item with no code of its own is declared
    under its category's code for the authority being filed with (ADR 0014).
*/
CREATE TABLE [dbo].[CategoryCustomsCode] (
    [CategoryId]    int              NOT NULL,
    [Authority]     int              NOT NULL,
    [Code]          varchar(10)      NOT NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,
    CONSTRAINT [PK_CategoryCustomsCode] PRIMARY KEY ([CategoryId], [Authority]),
    CONSTRAINT [CK_CategoryCustomsCode_Authority] CHECK ([Authority] >= 0 AND [Authority] <= 2),
    CONSTRAINT [FK_CategoryCustomsCode_Category] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[ItemCategory] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CategoryCustomsCode_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO
