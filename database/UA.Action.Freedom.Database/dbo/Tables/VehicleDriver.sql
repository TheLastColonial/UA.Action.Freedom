/*
    A convoy vehicle's crew. The table keeps its name, but a crew member is a Driver (0) or a
    Passenger (1) — a passenger can be any volunteer — and each row names the convoy:

      * One seat per person per convoy: UQ_VehicleDriver_Convoy_Person. A person cannot be on two
        vehicles of the same journey.
      * The key is (ConvoyId, Vin, PersonId), not (Vin, PersonId): a Returned vehicle can join a
        later convoy, and the crew of the earlier journey stays as history.
      * FK_VehicleDriver_Convoy is NO ACTION rather than CASCADE. Convoy already reaches this
        table through Vehicle (SET NULL, then CASCADE), and SQL Server refuses a second cascade
        path; ConvoyRepository.DeleteAsync and UnassignVehicleAsync clear the crew themselves,
        in the same transaction.
*/
CREATE TABLE [dbo].[VehicleDriver] (
    [ConvoyId]  int              NOT NULL,
    [Vin]       varchar(32)      NOT NULL,
    [PersonId]  uniqueidentifier NOT NULL,
    [Role]      int              NOT NULL CONSTRAINT [DF_VehicleDriver_Role] DEFAULT 0,
    [CreatedAt] datetime2        NOT NULL CONSTRAINT [DF_VehicleDriver_CreatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_VehicleDriver] PRIMARY KEY ([ConvoyId], [Vin], [PersonId]),
    CONSTRAINT [FK_VehicleDriver_Convoy] FOREIGN KEY ([ConvoyId]) REFERENCES [dbo].[Convoy] ([Id]),
    CONSTRAINT [FK_VehicleDriver_Vehicle] FOREIGN KEY ([Vin]) REFERENCES [dbo].[Vehicle] ([Vin]) ON DELETE CASCADE,
    CONSTRAINT [FK_VehicleDriver_Person] FOREIGN KEY ([PersonId]) REFERENCES [dbo].[Person] ([Id]),
    CONSTRAINT [CK_VehicleDriver_Role] CHECK ([Role] = 0 OR [Role] = 1),
    CONSTRAINT [UQ_VehicleDriver_Convoy_Person] UNIQUE ([ConvoyId], [PersonId])
);
GO

CREATE INDEX [IX_VehicleDriver_PersonId] ON [dbo].[VehicleDriver] ([PersonId]);
GO
