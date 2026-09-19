/*
    Volunteers — a split identity.

    A volunteer is two rows. dbo.Person is the identity — a random uniqueidentifier and nothing
    about the person — and it is what every foreign key points at: vehicle crews, manifest
    teams, who validated a box, who shelved it. dbo.PersonDetail holds the personal data and
    cascades from it.

    UK data protection (recommendations 4.8): erasing a volunteer deletes their PersonDetail row
    — a genuine delete of the personal data — and stamps Person.ErasedAt. The records they appear
    in keep a seat filled by an identity nothing links back to anyone, and read "Former
    volunteer". The key is a uniqueidentifier rather than an IDENTITY so a URL does not disclose
    how many volunteers the charity has; that is also what keeps an erased stub from being
    guessable. Never written to a log. Do not put personal data back on this table.
*/
CREATE TABLE [dbo].[Person] (
    [Id]        uniqueidentifier NOT NULL CONSTRAINT [PK_Person] PRIMARY KEY,
    [CreatedAt] datetime2(0)     NOT NULL CONSTRAINT [DF_Person_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [ErasedAt]  datetime2(0)     NULL
);
