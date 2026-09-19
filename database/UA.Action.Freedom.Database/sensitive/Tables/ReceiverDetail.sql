/*
    The delivery address and the contact for a receiver. Only ground_officer can read it, and
    freedom_app is explicitly DENY'd (Security/Permissions.sql), so the application identity
    cannot select a delivery address even if someone later adds a broad grant elsewhere.
*/
CREATE TABLE [sensitive].[ReceiverDetail] (
    [ReceiverRef]  uniqueidentifier NOT NULL CONSTRAINT [PK_ReceiverDetail] PRIMARY KEY
                   CONSTRAINT [FK_ReceiverDetail_Receiver] REFERENCES [dbo].[Receiver] ([ReceiverRef]),
    [ContactName]  nvarchar(200)    NOT NULL,
    [ContactPhone] nvarchar(50)     NOT NULL,
    [AddressLine1] nvarchar(200)    NOT NULL,
    [AddressLine2] nvarchar(200)    NULL,
    [City]         nvarchar(100)    NOT NULL,
    [PostCode]     nvarchar(20)     NULL,
    -- 4.4.5: delete this row a defined period after delivery is confirmed.
    [DeleteAfter]  datetime2(0)     NULL
);
