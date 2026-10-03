/*
    A distribution hub (a garage or warehouse). Its bays (dbo.Bay) are where a box is physically
    shelved once it reaches a depot.
*/
CREATE TABLE [dbo].[Location] (
    [Id]        int           NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Location] PRIMARY KEY,
    [Name]      nvarchar(200) NOT NULL,
    [House]     nvarchar(100) NULL,
    [Street]    nvarchar(200) NULL,
    [City]      nvarchar(100) NULL,
    [Country]   nvarchar(100) NULL,
    [Postcode]  nvarchar(20)  NULL,

    -- A distribution hub is a Location an Administrator has registered, with no status of its own
    -- (decision D36). Set under locations:write, which is Administrator only.
    [IsRegisteredHub] bit         NOT NULL CONSTRAINT [DF_Location_IsRegisteredHub] DEFAULT 0,
    [CreatedAt] datetime2(0)  NOT NULL CONSTRAINT [DF_Location_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] datetime2(0)  NOT NULL CONSTRAINT [DF_Location_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,
    CONSTRAINT [FK_Location_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
