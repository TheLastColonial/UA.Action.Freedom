/*
    Who a booking covers. People by id only, never by name: a booking is personal data about a volunteer, and
    erasing one must leave nothing of them in a booking's details. A guest is a link and is not stamped itself; the
    booking row records who last changed it, and a migration stamps the booking.

    No foreign key to the crew: the booking stays when its guest leaves (P13).
*/
CREATE TABLE [dbo].[AccommodationBookingGuest] (
    [BookingId] int              NOT NULL,
    [PersonId]  uniqueidentifier NOT NULL,

    CONSTRAINT [PK_AccommodationBookingGuest] PRIMARY KEY ([BookingId], [PersonId]),
    CONSTRAINT [FK_AccommodationBookingGuest_Booking] FOREIGN KEY ([BookingId])
        REFERENCES [dbo].[AccommodationBooking] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AccommodationBookingGuest_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id])
);
GO

-- Every erasure asks whether a record names the person, and an unindexed foreign key scans under lock.
CREATE INDEX [IX_AccommodationBookingGuest_PersonId] ON [dbo].[AccommodationBookingGuest] ([PersonId]);
GO
