/*
    Donors — a split identity, the same pattern as volunteers (ADR 0013, see dbo.Person).

    dbo.Donor is the anonymous key a donation points at: a random uniqueidentifier and nothing about the
    donor. The personal data is dbo.DonorDetail, which cascades from it. Erasing a donor deletes the
    detail — a genuine delete — and removes this row too unless a donation still names it, in which case
    it is stamped ErasedAt and the donation reads "Former donor". A donor is never a dbo.Person: a
    volunteer who also gives goods is two unrelated rows, so erasing one never erases the other.
    Do not put personal data back on this table.
*/
CREATE TABLE [dbo].[Donor] (
    [Id]        uniqueidentifier NOT NULL CONSTRAINT [PK_Donor] PRIMARY KEY,
    [CreatedAt] datetime2(0)     NOT NULL CONSTRAINT [DF_Donor_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [ErasedAt]  datetime2(0)     NULL
);
