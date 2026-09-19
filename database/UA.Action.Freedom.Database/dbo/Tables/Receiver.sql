/*
    Receivers — the split that matters most.

    dbo.Receiver is what the rest of the application joins on and what may appear on a document
    that crosses a border: an opaque reference, the organisation, and a region. Region is as
    precise as anything that travels gets (4.4.2). The delivery address and contact live in
    sensitive.ReceiverDetail, which only ground_officer can read.
*/
CREATE TABLE [dbo].[Receiver] (
    [ReceiverRef]  uniqueidentifier NOT NULL CONSTRAINT [PK_Receiver] PRIMARY KEY,
    [Organisation] nvarchar(200)    NOT NULL,
    [Region]       nvarchar(100)    NOT NULL,
    [CreatedAt]    datetime2(0)     NOT NULL CONSTRAINT [DF_Receiver_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]    datetime2(0)     NOT NULL CONSTRAINT [DF_Receiver_UpdatedAt] DEFAULT SYSUTCDATETIME()
);
