/*
    The drivers one insurance policy covers. Recording the policy names every driver the vehicle has
    at that moment; removing a driver from the crew removes them here in the same transaction, and
    the policy stays in cover for the rest. A driver added afterwards has no row here, which is how
    "uncovered" is derived: a crew driver with no row.

    Cascades from the policy, which cascades from the truck-list entry. PersonId is not a foreign
    key to dbo.Person on purpose: it only ever mirrors a crew seat, and the crew row owns that
    reference.
*/
CREATE TABLE [dbo].[ConvoyVehicleInsuranceDriver] (
    [ConvoyId] int              NOT NULL,
    [Vin]      varchar(32)      NOT NULL,
    [PersonId] uniqueidentifier NOT NULL,

    CONSTRAINT [PK_ConvoyVehicleInsuranceDriver] PRIMARY KEY ([ConvoyId], [Vin], [PersonId]),
    CONSTRAINT [FK_ConvoyVehicleInsuranceDriver_ConvoyVehicleInsurance] FOREIGN KEY ([ConvoyId], [Vin])
        REFERENCES [dbo].[ConvoyVehicleInsurance] ([ConvoyId], [Vin]) ON DELETE CASCADE
);
