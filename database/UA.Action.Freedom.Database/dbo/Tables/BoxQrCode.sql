/*
    A QR label ties the physical box to its record. The token is an opaque, non-enumerable Guid
    — the label may be inspected at a border, so it carries a box number, this token, what is
    in the box (category, quantity, expiry) and who signed it, in English and Ukrainian, and never
    a receiver, region, address, free text, value or donor (see the label renderer and
    docs/security/0011-label-review.md). Voiding a box revokes its code (ADR 0011).

    A box can be re-labelled: issuing a new code revokes the previous one, so at most one row per
    box has RevokedAt IS NULL. That is enforced in a transaction in
    BoxRepository.IssueQrCodeAsync.
*/
CREATE TABLE [dbo].[BoxQrCode] (
    [Token]     uniqueidentifier NOT NULL CONSTRAINT [PK_BoxQrCode] PRIMARY KEY,
    [BoxId]     int              NOT NULL,
    [IssuedAt]  datetime2(0)     NOT NULL CONSTRAINT [DF_BoxQrCode_IssuedAt] DEFAULT SYSUTCDATETIME(),
    [RevokedAt] datetime2(0)     NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    -- A label has no life outside its box: deleting the box takes its labels with it.
    CONSTRAINT [FK_BoxQrCode_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_BoxQrCode_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_BoxQrCode_BoxId] ON [dbo].[BoxQrCode] ([BoxId]);
GO
