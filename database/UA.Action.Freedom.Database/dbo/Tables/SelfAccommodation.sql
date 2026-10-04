/*
    A crew member arranging their own accommodation at one overnight stop, for example staying with family
    (O4, O30). The flag satisfies the accommodation requirement for that person at that stop only.
*/
CREATE TABLE [dbo].[SelfAccommodation] (
    [ConvoyId]      int              NOT NULL,
    [RoutePointId]  int              NOT NULL,
    [PersonId]      uniqueidentifier NOT NULL,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [PK_SelfAccommodation] PRIMARY KEY ([ConvoyId], [RoutePointId], [PersonId]),
    CONSTRAINT [FK_SelfAccommodation_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_SelfAccommodation_RouteStop] FOREIGN KEY ([ConvoyId], [RoutePointId])
        REFERENCES [dbo].[ConvoyRouteStop] ([ConvoyId], [RoutePointId]),
    CONSTRAINT [FK_SelfAccommodation_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [FK_SelfAccommodation_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE INDEX [IX_SelfAccommodation_PersonId] ON [dbo].[SelfAccommodation] ([PersonId]);
GO

CREATE INDEX [IX_SelfAccommodation_LastChangedBy] ON [dbo].[SelfAccommodation] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
