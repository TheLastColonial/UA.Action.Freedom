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
    [CreatedAt] datetime2(0)  NOT NULL CONSTRAINT [DF_Location_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] datetime2(0)  NOT NULL CONSTRAINT [DF_Location_UpdatedAt] DEFAULT SYSUTCDATETIME()
);
