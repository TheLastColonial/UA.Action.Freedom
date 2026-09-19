/*
    A packed container of items with a confirmed weight, a current location and a target
    receiver. ValidatedByPersonId + ValidatedAt are an audit artefact rather than a status flag:
    a Loader physically checks the contents and weighs the box, and that check is the trust
    boundary between the donor and Ukrainian Action. The application refuses to change a
    validated box, so those two columns are only ever written once.

    ReceiverRef is the opaque reference into dbo.Receiver and nothing more. The delivery address
    lives in sensitive.ReceiverDetail and never comes near cargo (4.4).

    LocationId is the box's current whereabouts — a UK depot, routine, not sensitive — and is
    independent of which bay it has been shelved in (dbo.BoxBayAssignment): a box can be checked
    in at a location before a Loader gets round to placing it in a bay.
*/
CREATE TABLE [dbo].[Box] (
    [Id]                  int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Box] PRIMARY KEY,
    [WeightKg]            int              NOT NULL CONSTRAINT [DF_Box_WeightKg] DEFAULT 0,

    -- Dimensions: optional, set alongside WeightKg at validation (a Loader is physically looking
    -- at the box then). Decimal, not int, to the nearest hundredth of a centimetre.
    [WidthCm]             decimal(10,2)    NULL,
    [DepthCm]             decimal(10,2)    NULL,
    [HeightCm]            decimal(10,2)    NULL,

    [ReceiverRef]         uniqueidentifier NULL,
    [LocationId]          int              NULL,
    [ValidatedByPersonId] uniqueidentifier NULL,
    [ValidatedAt]         datetime2(0)     NULL,
    [CreatedAt]           datetime2(0)     NOT NULL CONSTRAINT [DF_Box_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]           datetime2(0)     NOT NULL CONSTRAINT [DF_Box_UpdatedAt] DEFAULT SYSUTCDATETIME(),

    -- A receiver cannot be deleted out from under cargo already routed to it.
    CONSTRAINT [FK_Box_Receiver] FOREIGN KEY ([ReceiverRef]) REFERENCES [dbo].[Receiver] ([ReceiverRef]),

    -- Likewise a location: a depot cannot be deleted out from under boxes that are in it.
    CONSTRAINT [FK_Box_Location] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[Location] ([Id]),

    -- The validator is a volunteer on file. NO ACTION on delete: a volunteer who leaves must not
    -- take the record of what they signed for with them.
    CONSTRAINT [FK_Box_ValidatedBy] FOREIGN KEY ([ValidatedByPersonId]) REFERENCES [dbo].[Person] ([Id]),

    -- Validation is one event, so its two halves are written together or not at all.
    CONSTRAINT [CK_Box_ValidationIsWholeOrAbsent] CHECK (
        ([ValidatedByPersonId] IS NULL AND [ValidatedAt] IS NULL)
        OR ([ValidatedByPersonId] IS NOT NULL AND [ValidatedAt] IS NOT NULL))
);
