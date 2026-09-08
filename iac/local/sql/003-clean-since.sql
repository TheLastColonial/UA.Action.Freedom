/*
    Freedom scoped cleanup — deletes only the rows created since a marker time.

    Same intent as 002-reset-data.sql but bounded: it removes what a test run created and
    leaves rows a developer made by hand before the run alone. Used by the Playwright
    teardown project (web/e2e/global.teardown.ts), which captures the marker in
    web/e2e/db.setup.ts before the suite runs. Not run by OpenTofu.

    Every root table carries `CreatedAt datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME()`, so
    "created since the marker" is `CreatedAt >= @mark`. Child tables without a CreatedAt
    (ConvoyRouteStop, BoxItem, BoxQrCode, ManifestBox, ManifestDriverTeam) go by ON DELETE
    CASCADE from their parent. sensitive.ReceiverDetail has no CreatedAt either, so it is
    scoped through its receiver; the access log is scoped by its own ReadAt.

    Invoke with:  sqlcmd ... -v Mark="2026-09-08T12:00:00.0000000" -i /sql/003-clean-since.sql
    Run as `sa` (see 002-reset-data.sql for why). DELETE order is load-bearing — same
    NO ACTION foreign keys as 002.

    The BDD suite does the equivalent from C# (Support/DataResetHook.cs) with a parameterised
    command rather than this file, because it was asked to use a direct SQL connection and
    sqlcmd variable substitution is not available there. Keep the two in step.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

USE Freedom;
GO

DECLARE @mark datetime2(7) = CONVERT(datetime2(7), N'$(Mark)', 126);

BEGIN TRANSACTION;

IF OBJECT_ID(N'sensitive.ReceiverDetailAccessLog', N'U') IS NOT NULL
    DELETE FROM sensitive.ReceiverDetailAccessLog WHERE ReadAt >= @mark;

IF OBJECT_ID(N'sensitive.ReceiverDetail', N'U') IS NOT NULL
    DELETE FROM sensitive.ReceiverDetail
    WHERE ReceiverRef IN (SELECT ReceiverRef FROM dbo.Receiver WHERE CreatedAt >= @mark);

IF OBJECT_ID(N'dbo.Manifest', N'U') IS NOT NULL DELETE FROM dbo.Manifest WHERE CreatedAt >= @mark;
IF OBJECT_ID(N'dbo.Box',      N'U') IS NOT NULL DELETE FROM dbo.Box      WHERE CreatedAt >= @mark;
IF OBJECT_ID(N'dbo.Vehicle',  N'U') IS NOT NULL DELETE FROM dbo.Vehicle  WHERE CreatedAt >= @mark;
IF OBJECT_ID(N'dbo.Convoy',   N'U') IS NOT NULL DELETE FROM dbo.Convoy   WHERE CreatedAt >= @mark;
IF OBJECT_ID(N'dbo.Person',   N'U') IS NOT NULL DELETE FROM dbo.Person   WHERE CreatedAt >= @mark;
IF OBJECT_ID(N'dbo.Receiver', N'U') IS NOT NULL DELETE FROM dbo.Receiver WHERE CreatedAt >= @mark;

COMMIT;
GO

PRINT 'Freedom scoped cleanup complete.';
GO
