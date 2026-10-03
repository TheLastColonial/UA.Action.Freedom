/*
    One donor, one drop-off or consignment, many items (ADR 0013). BoxItem.DonationId points here, so
    every item can be attributed to the donor who gave it. The donation outlives its donor's erasure:
    DonorId keeps pointing at the anonymous dbo.Donor row, and the donation reads "Former donor".
    No cascade from the donor: a donation is never deleted by erasing anyone.
*/
CREATE TABLE [dbo].[Donation] (
    [Id]            int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Donation] PRIMARY KEY,
    [DonorId]       uniqueidentifier NOT NULL,
    [ReceivedOn]    date             NOT NULL,
    [Notes]         nvarchar(1000)   NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [FK_Donation_Donor] FOREIGN KEY ([DonorId]) REFERENCES [dbo].[Donor] ([Id]),
    CONSTRAINT [FK_Donation_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_Donation_DonorId_ReceivedOn] ON [dbo].[Donation] ([DonorId], [ReceivedOn] DESC, [Id] DESC);
GO
