/*
    A 1m by 1m storage area within a location, identified by a short code that only has to be
    unique within its own location — two depots may each have a bay called "A1".
*/
CREATE TABLE [dbo].[Bay] (
    [Id]         int          NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Bay] PRIMARY KEY,
    [LocationId] int          NOT NULL,
    [Code]       nvarchar(20) NOT NULL,
    [CreatedAt]  datetime2(0) NOT NULL CONSTRAINT [DF_Bay_CreatedAt] DEFAULT SYSUTCDATETIME(),

    -- A location's bays go with it: there is no reason to keep an orphaned bay around.
    CONSTRAINT [FK_Bay_Location] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[Location] ([Id]) ON DELETE CASCADE,

    -- Per-location uniqueness, not global: "A1" at Coventry and "A1" at London are different bays.
    CONSTRAINT [UQ_Bay_Location_Code] UNIQUE ([LocationId], [Code])
);
