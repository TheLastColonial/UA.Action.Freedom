/*
    A stay booked for the crew at one route point (P2, P8). It may cover several people who share, named in
    AccommodationBookingGuest. CostGbp is optional and is read into the Hotel line of the convoy budget (plan 12).

    Cancelling is a stamp (Cancelled), not a delete: the booking, its cost and who made it stay on record, and a
    cancelled booking covers nobody. A booking outlives its guest leaving the crew (P13): nothing here cascades from
    the crew, and the leftover booking is reported to the Dispatcher as a warning and a task (P16).

    The composite foreign key to the route stop makes "a point of THIS convoy" a fact of the schema, and, being
    NO ACTION, is what stops an edit of the route removing a point a booking stays at.
*/
CREATE TABLE [dbo].[AccommodationBooking] (
    [Id]            int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AccommodationBooking] PRIMARY KEY,
    [ConvoyId]      int              NOT NULL,
    [RoutePointId]  int              NOT NULL,
    [Provider]      nvarchar(200)    NOT NULL,
    [Reference]     nvarchar(100)    NULL,
    [CheckIn]       datetime2(0)     NOT NULL,
    [CheckOut]      datetime2(0)     NOT NULL,
    [Details]       nvarchar(1000)   NULL,
    [CostGbp]       decimal(10,2)    NULL,
    [Cancelled]     bit              NOT NULL CONSTRAINT [DF_AccommodationBooking_Cancelled] DEFAULT 0,
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,

    CONSTRAINT [FK_AccommodationBooking_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AccommodationBooking_RouteStop] FOREIGN KEY ([ConvoyId], [RoutePointId])
        REFERENCES [dbo].[ConvoyRouteStop] ([ConvoyId], [RoutePointId]),
    CONSTRAINT [FK_AccommodationBooking_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_AccommodationBooking_Stay] CHECK ([CheckOut] >= [CheckIn]),
    CONSTRAINT [CK_AccommodationBooking_Cost] CHECK ([CostGbp] IS NULL OR [CostGbp] >= 0)
);
GO

CREATE INDEX [IX_AccommodationBooking_Convoy] ON [dbo].[AccommodationBooking] ([ConvoyId], [RoutePointId]);
GO

CREATE INDEX [IX_AccommodationBooking_LastChangedBy] ON [dbo].[AccommodationBooking] ([LastChangedBy]) WHERE [LastChangedBy] IS NOT NULL;
GO
