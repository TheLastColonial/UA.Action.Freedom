/*
    A donor's personal data — see dbo.Donor for why it is a separate row. Never written to a log.
*/
CREATE TABLE [dbo].[DonorDetail] (
    [DonorId]       uniqueidentifier NOT NULL CONSTRAINT [PK_DonorDetail] PRIMARY KEY,
    [Name]          nvarchar(200)    NOT NULL,
    [Email]         nvarchar(254)    NULL,
    [Phone]         nvarchar(50)     NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [FK_DonorDetail_Donor] FOREIGN KEY ([DonorId]) REFERENCES [dbo].[Donor] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DonorDetail_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_DonorDetail_Name] ON [dbo].[DonorDetail] ([Name], [DonorId]);
GO
