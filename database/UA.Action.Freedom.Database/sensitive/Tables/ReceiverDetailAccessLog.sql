-- Every read of a full address is audited (4.4.3). This matters more than the data: there is
-- deliberately no foreign key to the receiver, so the trail outlives the address it describes.
CREATE TABLE [sensitive].[ReceiverDetailAccessLog] (
    [Id]          bigint           IDENTITY(1,1) CONSTRAINT [PK_ReceiverDetailAccessLog] PRIMARY KEY,
    [ReceiverRef] uniqueidentifier NOT NULL,
    [PrincipalId] nvarchar(200)    NOT NULL,
    [ReadAt]      datetime2(0)     NOT NULL CONSTRAINT [DF_ReceiverDetailAccessLog_ReadAt] DEFAULT SYSUTCDATETIME(),
    [Reason]      nvarchar(400)    NULL
);
