/*
    Where a box currently sits within its location, and the history of where it has sat.

    Mirrors dbo.BoxQrCode's issue/revoke shape: assigning a bay vacates whatever bay the box was
    already in, so at most one row per box has VacatedAt IS NULL, enforced by a transaction in
    BoxRepository.AssignBayAsync. Vacated rows are kept, not deleted: a Loader asking "where has
    this box been" is a real question once a convoy is being packed.

    A bay may hold several boxes at once — there is no uniqueness on BayId here.
*/
CREATE TABLE [dbo].[BoxBayAssignment] (
    [Id]                 int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_BoxBayAssignment] PRIMARY KEY,
    [BoxId]              int              NOT NULL,
    [BayId]              int              NOT NULL,
    [AssignedByPersonId] uniqueidentifier NOT NULL,
    [AssignedAt]         datetime2(0)     NOT NULL CONSTRAINT [DF_BoxBayAssignment_AssignedAt] DEFAULT SYSUTCDATETIME(),
    [VacatedAt]          datetime2(0)     NULL,

    -- An assignment has no life outside its box: deleting the box takes its bay history with it.
    -- Bays, unlike boxes, are not deleted casually, so NO ACTION there — deleting a bay that
    -- still has history should fail loudly rather than silently orphan rows.
    CONSTRAINT [FK_BoxBayAssignment_Box] FOREIGN KEY ([BoxId]) REFERENCES [dbo].[Box] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_BoxBayAssignment_Bay] FOREIGN KEY ([BayId]) REFERENCES [dbo].[Bay] ([Id]),
    CONSTRAINT [FK_BoxBayAssignment_Person] FOREIGN KEY ([AssignedByPersonId]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_BoxBayAssignment_BoxId] ON [dbo].[BoxBayAssignment] ([BoxId]);
GO

CREATE INDEX [IX_BoxBayAssignment_BayId] ON [dbo].[BoxBayAssignment] ([BayId]);
GO
