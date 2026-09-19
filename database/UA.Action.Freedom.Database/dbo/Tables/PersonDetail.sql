/*
    A volunteer's personal data — see dbo.Person for why it is a separate row.

    The domain models a Driver as a subtype of Person; the database keeps IsDriver on the
    detail, because everything a driver adds (Committed) is two columns rather than a second
    identity.
*/
CREATE TABLE [dbo].[PersonDetail] (
    [PersonId]    uniqueidentifier NOT NULL CONSTRAINT [PK_PersonDetail] PRIMARY KEY,
    [FirstName]   nvarchar(100)    NOT NULL,
    [LastName]    nvarchar(100)    NOT NULL,
    [DateOfBirth] datetime2(0)     NOT NULL,
    [Joined]      datetime2(0)     NOT NULL,
    [Phone]       nvarchar(50)     NULL,
    [IsDriver]    bit              NOT NULL CONSTRAINT [DF_PersonDetail_IsDriver] DEFAULT 0,
    [Committed]   bit              NOT NULL CONSTRAINT [DF_PersonDetail_Committed] DEFAULT 0,
    [UpdatedAt]   datetime2(0)     NOT NULL CONSTRAINT [DF_PersonDetail_UpdatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [FK_PersonDetail_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id]) ON DELETE CASCADE
);
GO

-- The dispatcher's shortlist is "drivers, by name". Everything else pages the full roster in
-- the same order, so one index serves both reads.
CREATE INDEX [IX_PersonDetail_IsDriver_Name] ON [dbo].[PersonDetail] ([IsDriver], [LastName], [FirstName], [PersonId]);
GO
