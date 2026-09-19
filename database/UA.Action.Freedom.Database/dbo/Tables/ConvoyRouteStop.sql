/*
    A convoy's route. A child table rather than a column of stops, because Sequence is what
    makes it a journey rather than a bag of addresses. Sequence is dense and 1-based; the
    application renumbers on write, so nothing here has to trust the caller's numbering.
*/
CREATE TABLE [dbo].[ConvoyRouteStop] (
    [ConvoyId] int           NOT NULL,
    [Sequence] int           NOT NULL,
    [House]    nvarchar(100) NULL,
    [Street]   nvarchar(200) NULL,
    [City]     nvarchar(100) NULL,
    [Country]  nvarchar(100) NULL,
    [Postcode] nvarchar(20)  NOT NULL CONSTRAINT [DF_ConvoyRouteStop_Postcode] DEFAULT '',

    CONSTRAINT [PK_ConvoyRouteStop] PRIMARY KEY ([ConvoyId], [Sequence]),
    -- The route has no life of its own: deleting the convoy takes it with it, which is also
    -- what stops a cancelled convoy leaving orphan stops behind.
    CONSTRAINT [FK_ConvoyRouteStop_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]) ON DELETE CASCADE
);
