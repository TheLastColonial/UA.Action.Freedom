/*
    Receivers — the split that matters most.

    dbo.Receiver is what the rest of the application joins on and what may appear on a document
    that crosses a border: an opaque reference, the organisation, and a region. Region is as
    precise as anything that travels gets (4.4.2). The delivery address and contact live in
    sensitive.ReceiverDetail, which only ground_officer can read.

    Status is whether the Receiver may be sent to: 0 pending, 1 registered, 2 suspended, 3 expired
    (Domain ReceiverStatus). Only an Administrator changes it, only registered lets a box or a
    vehicle name the Receiver (ADR 0012). It is not sensitive, and it deliberately says nothing
    about what kind of body the Receiver is (decision D33) — do not add a column that does.
*/
CREATE TABLE [dbo].[Receiver] (
    [ReceiverRef]  uniqueidentifier NOT NULL CONSTRAINT [PK_Receiver] PRIMARY KEY,
    [Organisation] nvarchar(200)    NOT NULL,
    [Region]       nvarchar(100)    NOT NULL,
    [Status]       int              NOT NULL CONSTRAINT [DF_Receiver_Status] DEFAULT 0,
    [CreatedAt]    datetime2(0)     NOT NULL CONSTRAINT [DF_Receiver_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]    datetime2(0)     NOT NULL CONSTRAINT [DF_Receiver_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,
    CONSTRAINT [CK_Receiver_Status] CHECK ([Status] >= 0 AND [Status] <= 3),
    CONSTRAINT [FK_Receiver_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
