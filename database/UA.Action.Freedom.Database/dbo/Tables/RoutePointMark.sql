/*
    WHAT THE CONVOY LEADER REPORTED ABOUT A ROUTE POINT (ADR 0016). A point is Reached, or a border is Crossed.

    There is no position here and there never will be (O20): only that somebody said a point was reached, and when
    they entered it (O27). RecordedAt is the time of ENTRY, not of arrival, and nothing else is kept. OnBehalfOfLeader
    is set when the person who entered it is not the sitting Convoy Leader: that is the Dispatcher marking for the
    leader, and RecordedBy names them.

    Vin is NULL for a mark on the whole convoy and set where vehicles split. It is varchar(32): pass it with
    SqlKey.Of(...). A mark is one fact once made, so two filtered unique indexes (the whole convoy, and one vehicle) refuse
    a second; the mark is never edited or deleted, which is why the table has RecordedBy / RecordedAt in place of
    LastChangedBy / LastChangedAt.

    The composite key to ConvoyRouteStop is NO ACTION on purpose: a mark pins its route point, so removing the point from
    the route is refused as "in use" (409 route-point-in-use), like accommodation. Kind is an int, in SQL Server's
    normalised CHECK form (>= AND <=, never BETWEEN).
*/
CREATE TABLE [dbo].[RoutePointMark] (
    [Id]               int              NOT NULL IDENTITY(1,1) CONSTRAINT [PK_RoutePointMark] PRIMARY KEY,
    [ConvoyId]         int              NOT NULL,
    [RoutePointId]     int              NOT NULL,
    -- 0 Reached, 1 Crossed (a Border point only; the application checks the kind of point).
    [Kind]             int              NOT NULL,
    [Vin]              varchar(32)      NULL,
    [OnBehalfOfLeader] bit              NOT NULL CONSTRAINT [DF_RoutePointMark_OnBehalf] DEFAULT 0,
    [RecordedBy]       uniqueidentifier NULL,
    [RecordedAt]       datetime2(0)     NOT NULL CONSTRAINT [DF_RoutePointMark_RecordedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [CK_RoutePointMark_Kind] CHECK ([Kind] >= 0 AND [Kind] <= 1),
    CONSTRAINT [FK_RoutePointMark_RoutePoint] FOREIGN KEY ([ConvoyId], [RoutePointId]) REFERENCES [dbo].[ConvoyRouteStop] ([ConvoyId], [RoutePointId]),
    CONSTRAINT [FK_RoutePointMark_ConvoyVehicle] FOREIGN KEY ([ConvoyId], [Vin]) REFERENCES [dbo].[ConvoyVehicle] ([ConvoyId], [Vin]),
    CONSTRAINT [FK_RoutePointMark_RecordedBy] FOREIGN KEY ([RecordedBy]) REFERENCES [dbo].[Person] ([Id])
);
GO

CREATE UNIQUE INDEX [UX_RoutePointMark_Convoy] ON [dbo].[RoutePointMark] ([ConvoyId], [RoutePointId], [Kind]) WHERE [Vin] IS NULL;
GO

CREATE UNIQUE INDEX [UX_RoutePointMark_Vehicle] ON [dbo].[RoutePointMark] ([ConvoyId], [RoutePointId], [Kind], [Vin]) WHERE [Vin] IS NOT NULL;
GO

CREATE INDEX [IX_RoutePointMark_ConvoyVehicle] ON [dbo].[RoutePointMark] ([ConvoyId], [Vin]) WHERE [Vin] IS NOT NULL;
GO

CREATE INDEX [IX_RoutePointMark_RecordedBy] ON [dbo].[RoutePointMark] ([RecordedBy]) WHERE [RecordedBy] IS NOT NULL;
GO
