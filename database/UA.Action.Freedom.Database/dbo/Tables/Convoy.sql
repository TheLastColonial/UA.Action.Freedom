/*
    A convoy is the unit that is planned; the manifest is the unit that is executed per vehicle.
    Roughly one convoy a month, never concurrent (recommendations 5.2), so an int IDENTITY is a
    generous key and it keeps the /convoys/{id} route readable.

    TruckListPublishedAt is the gate in docs/process.puml: Truck List Created -> Truck List
    Published -> Manifest Proposed. Publication is one-way and the application refuses to change
    the vehicle list afterwards. NULL means "still being planned".

    ArrivedAt is stamped by POST /convoys/{id}/arrive once every vehicle on it has a finished
    manifest. Both are write-once, absent from every UPDATE an ordinary edit issues.

    CrossingMode is how the convoy crosses the Channel, and it is here rather than in configuration
    because it decides two things an ICS2 Entry Summary Declaration cannot be filed without: the
    mode-of-transport code (Ferry -> 1 maritime, Shuttle -> 3 road; rail is not accepted at the
    Brexit Smart Border) and whether a vessel has to be named. Customs:RouteId is still one value
    for the whole application, and gotchas 9 notes it becomes a property of the convoy if convoys
    ever cross by more than one route -- this is the first half of that happening.

    int rather than tinyint, so the column type lines up with the CLR type of the enum Dapper
    hydrates. The CHECK is written in SQL Server's normalised form (>= AND <=, never BETWEEN) or
    every publish recreates it and the Database workflow fails on a non-empty DeployReport.

    Neither CrossingMode nor VesselImo is write-once: the crossing may change until the ENS is
    filed. Afterwards it may not, because mode of transport and the vessel IMO are both
    non-amendable in ICS2 -- correcting one means invalidating the declaration and refiling.
*/
CREATE TABLE [dbo].[Convoy] (
    [Id]                   int          NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Convoy] PRIMARY KEY,
    [Start]                datetime2(0) NOT NULL,
    [ExpectedEnd]          datetime2(0) NOT NULL,
    [CrossingMode]         int          NOT NULL CONSTRAINT [DF_Convoy_CrossingMode] DEFAULT 0,
    [VesselImo]            varchar(10)  NULL,
    [TruckListPublishedAt] datetime2(0) NULL,
    [ArrivedAt]            datetime2(0) NULL,
    [CreatedAt]            datetime2(0) NOT NULL CONSTRAINT [DF_Convoy_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]            datetime2(0) NOT NULL CONSTRAINT [DF_Convoy_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    [LastChangedBy] uniqueidentifier NULL,
    [LastChangedAt] datetime2(0)     NULL,
    CONSTRAINT [CK_Convoy_CrossingMode] CHECK ([CrossingMode] >= 0 AND [CrossingMode] <= 1),
    CONSTRAINT [FK_Convoy_LastChangedBy] FOREIGN KEY ([LastChangedBy]) REFERENCES [dbo].[Person] ([Id])
);
