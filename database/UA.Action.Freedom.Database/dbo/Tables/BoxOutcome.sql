/*
    HOW A BOX ENDED ON A CONVOY (ADR 0015). Delivered, Accepted, Seized, ReturnedToHub or
    Undeliverable; "lost" is Undeliverable with that as the reason.

    Append-only with a current marker: a change of outcome clears IsCurrent on the old row and
    inserts the new one in the same transaction, so the history of a box on a convoy is kept and
    the filtered unique index is the rule that there is one current outcome per box per convoy.
    That is why the table has RecordedBy / RecordedAt in place of LastChangedBy / LastChangedAt.

    Vin is here because a returned box loses its allocation, and the report and the acceptance of a
    goods list still need to say which vehicle it travelled on. Vin is varchar(32): pass it with
    SqlKey.Of(...). Reason is one of a bounded set of codes, never free text. HubLocationId is a
    location, never a Receiver or an address.

    Outcome is an int, in SQL Server's normalised CHECK form (>= AND <=, never BETWEEN).
    Everything is NO ACTION: a box, convoy or hub that an outcome names cannot be deleted from under it.
*/
CREATE TABLE [dbo].[BoxOutcome] (
    [Id]            int          NOT NULL IDENTITY(1,1) CONSTRAINT [PK_BoxOutcome] PRIMARY KEY,
    [BoxId]         int          NOT NULL,
    [ConvoyId]      int          NOT NULL,
    [Vin]           varchar(32)  NOT NULL,
    [Outcome]       int          NOT NULL,
    [Reason]        varchar(32)  NULL,
    [HubLocationId] int          NULL,
    [IsCurrent]     bit          NOT NULL CONSTRAINT [DF_BoxOutcome_IsCurrent] DEFAULT 1,
    [RecordedBy]    uniqueidentifier NULL,
    [RecordedAt]    datetime2(0) NOT NULL CONSTRAINT [DF_BoxOutcome_RecordedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [CK_BoxOutcome_Outcome] CHECK ([Outcome] >= 0 AND [Outcome] <= 4),
    CONSTRAINT [FK_BoxOutcome_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]),
    CONSTRAINT [FK_BoxOutcome_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin]) REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]),
    CONSTRAINT [FK_BoxOutcome_HubLocation] FOREIGN KEY ([HubLocationId]) REFERENCES [dbo].[Location] ([Id]),
    CONSTRAINT [FK_BoxOutcome_RecordedBy] FOREIGN KEY ([RecordedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE UNIQUE INDEX [UX_BoxOutcome_Current] ON [dbo].[BoxOutcome] ([BoxId], [ConvoyId]) WHERE [IsCurrent] = 1;
GO

CREATE INDEX [IX_BoxOutcome_ConvoyVehicle] ON [dbo].[BoxOutcome] ([ConvoyId], [Vin]);
GO

CREATE INDEX [IX_BoxOutcome_HubLocation] ON [dbo].[BoxOutcome] ([HubLocationId]) WHERE [HubLocationId] IS NOT NULL;
GO

CREATE INDEX [IX_BoxOutcome_RecordedBy] ON [dbo].[BoxOutcome] ([RecordedBy]) WHERE [RecordedBy] IS NOT NULL;
GO
