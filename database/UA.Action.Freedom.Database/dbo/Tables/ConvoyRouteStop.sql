/*
    A convoy's route. A child table rather than a column of stops, because Sequence is what
    makes it a journey rather than a bag of addresses. Sequence is dense and 1-based; the
    application renumbers on write, so nothing here has to trust the caller's numbering.

    A stop is a route POINT with an identity of its own (RoutePointId). Accommodation, progress marks
    and crossings point at it, and it survives an edit of the route: saving a route merges by id
    rather than deleting and re-inserting, so Sequence is the mutable part and the id is not.
*/
CREATE TABLE [dbo].[ConvoyRouteStop] (
    [RoutePointId] int       IDENTITY(1,1) NOT NULL,
    [ConvoyId] int           NOT NULL,
    [Sequence] int           NOT NULL,
    [Name]     nvarchar(100) NOT NULL CONSTRAINT [DF_ConvoyRouteStop_Name] DEFAULT '',
    -- 0 Stop, 1 Overnight (only because the Dispatcher flagged it, P15), 2 Border, 3 Hub.
    [Kind]     int           NOT NULL CONSTRAINT [DF_ConvoyRouteStop_Kind] DEFAULT 0,
    -- The customs authority a Border point is a crossing for: 0 UK, 1 EU, 2 UA. NULL for every other kind.
    [Authority] int          NULL,
    [House]    nvarchar(100) NULL,
    [Street]   nvarchar(200) NULL,
    [City]     nvarchar(100) NULL,
    [Country]  nvarchar(100) NULL,
    -- The ISO 3166-1 alpha-2 code for Country, which is free text and always will be: a stop is
    -- written by a dispatcher planning a journey, not picked from a list. An ENS declares its
    -- countries of routing as codes, so the code is stored beside the name rather than guessed
    -- from it at filing time. NULL means nobody has supplied one, and the filing sheet says so.
    [CountryCode] char(2)    NULL,
    [Postcode] nvarchar(20)  NOT NULL CONSTRAINT [DF_ConvoyRouteStop_Postcode] DEFAULT '',
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [PK_ConvoyRouteStop] PRIMARY KEY ([RoutePointId]),
    CONSTRAINT [UQ_ConvoyRouteStop_Convoy_Sequence] UNIQUE ([ConvoyId], [Sequence]),
    CONSTRAINT [CK_ConvoyRouteStop_Kind] CHECK ([Kind] >= 0 AND [Kind] <= 3),
    CONSTRAINT [CK_ConvoyRouteStop_Authority] CHECK ([Authority] >= 0 AND [Authority] <= 2),
    -- The route has no life of its own: deleting the convoy takes it with it, which is also
    -- what stops a cancelled convoy leaving orphan stops behind.
    CONSTRAINT [FK_ConvoyRouteStop_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ConvoyRouteStop_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
