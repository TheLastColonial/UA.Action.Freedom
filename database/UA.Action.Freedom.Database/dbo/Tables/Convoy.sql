/*
    A convoy is the unit that is planned; the manifest is the unit that is executed per vehicle.
    Roughly one convoy a month, never concurrent (recommendations 5.2), so an int IDENTITY is a
    generous key and it keeps the /convoys/{id} route readable.

    TruckListPublishedAt is the gate in docs/process.puml: Truck List Created -> Truck List
    Published -> Manifest Proposed. Publication is one-way and the application refuses to change
    the vehicle list afterwards. NULL means "still being planned".

    ArrivedAt is stamped by POST /convoys/{id}/arrive once every vehicle on it has a finished
    manifest. Both are write-once, absent from every UPDATE an ordinary edit issues.
*/
CREATE TABLE [dbo].[Convoy] (
    [Id]                   int          NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Convoy] PRIMARY KEY,
    [Start]                datetime2(0) NOT NULL,
    [ExpectedEnd]          datetime2(0) NOT NULL,
    [TruckListPublishedAt] datetime2(0) NULL,
    [ArrivedAt]            datetime2(0) NULL,
    [CreatedAt]            datetime2(0) NOT NULL CONSTRAINT [DF_Convoy_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]            datetime2(0) NOT NULL CONSTRAINT [DF_Convoy_UpdatedAt] DEFAULT SYSUTCDATETIME()
);
