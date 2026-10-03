/*
    What kind of thing a donated item is (ADR 0014, D6). A fixed category is built in and cannot be
    removed; the Administrator maintains the rest and the mapping to customs codes (O31).

    HazardClass is the ADR class 1 to 9, NULL for goods that are not dangerous. IsNotCarried marks goods
    the convoy will not take (gas, lithium batteries, flammables, D21): adding one warns at once.
    WarnWithinDays is how close to expiry an item counts as short-dated; NULL means no warning. The
    thresholds are unverified (D25), which is why they are data an Administrator can change and not code.
    NameUk starts empty and is filled by the translation work in plan 16.
*/
CREATE TABLE [dbo].[ItemCategory] (
    [Id]             int            NOT NULL IDENTITY(1, 1) CONSTRAINT [PK_ItemCategory] PRIMARY KEY,
    [NameEn]         nvarchar(100)  NOT NULL,
    [NameUk]         nvarchar(100)  NOT NULL CONSTRAINT [DF_ItemCategory_NameUk] DEFAULT N'',
    [IsFixed]        bit            NOT NULL CONSTRAINT [DF_ItemCategory_IsFixed] DEFAULT 0,
    [HazardClass]    int            NULL,
    [IsSensitive]    bit            NOT NULL CONSTRAINT [DF_ItemCategory_IsSensitive] DEFAULT 0,
    [IsNotCarried]   bit            NOT NULL CONSTRAINT [DF_ItemCategory_IsNotCarried] DEFAULT 0,
    [WarnWithinDays] int            NULL,
    [LastChangedBy]  uniqueidentifier NULL,
    [LastChangedAt]  datetime2(0)   NULL,
    CONSTRAINT [UQ_ItemCategory_NameEn] UNIQUE ([NameEn]),
    CONSTRAINT [CK_ItemCategory_HazardClass] CHECK ([HazardClass] >= 1 AND [HazardClass] <= 9),
    CONSTRAINT [CK_ItemCategory_WarnWithinDays] CHECK ([WarnWithinDays] >= 0),
    CONSTRAINT [FK_ItemCategory_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO
