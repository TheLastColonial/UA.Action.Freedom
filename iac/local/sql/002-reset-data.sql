/*
    Freedom data reset — empties every application table without touching the schema.

    This is the deliberate "back to a known-good state" tool for local development. It is NOT
    run by OpenTofu and is NOT part of provisioning: `../../tofu/database.tf` applies
    001-schemas.sql only. Run this by hand (or through ../reset-data.ps1 / ../reset-data.sh)
    when a local database has accumulated rows from earlier test runs or manual poking and you
    want it clean again — without the heavier `docker compose down -v`, which also drops the
    Keycloak realm and forces another `tofu apply`.

    Run as `sa`. `sa` is sysadmin and bypasses permission checks, which is what lets it delete
    from `sensitive.*` — `freedom_app` is DENY'd SELECT there and has no reason to delete it.
    The compose healthcheck and the Integration suite's default connection string both already
    connect as `sa`, so the password is `MSSQL_SA_PASSWORD` from local/.env.

    Idempotent and correct on an empty/fresh database: every statement is guarded, DELETE on an
    empty table is a no-op, and the identity reseeds are skipped unless a table has actually
    had inserts. Never touches schemas, roles, logins or grants.

    The DELETE order is load-bearing. Several foreign keys are NO ACTION rather than CASCADE
    (sensitive.ReceiverDetail -> Receiver, Box -> Receiver/Person, Manifest -> Vehicle/Convoy),
    so a child must be gone before its parent. Keep the order if you edit this.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

USE Freedom;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'sensitive.ReceiverDetailAccessLog', N'U') IS NOT NULL DELETE FROM sensitive.ReceiverDetailAccessLog;
IF OBJECT_ID(N'sensitive.ReceiverDetail',          N'U') IS NOT NULL DELETE FROM sensitive.ReceiverDetail;

IF OBJECT_ID(N'dbo.ManifestBox',        N'U') IS NOT NULL DELETE FROM dbo.ManifestBox;
IF OBJECT_ID(N'dbo.ManifestDriverTeam', N'U') IS NOT NULL DELETE FROM dbo.ManifestDriverTeam;
IF OBJECT_ID(N'dbo.BoxItem',            N'U') IS NOT NULL DELETE FROM dbo.BoxItem;
IF OBJECT_ID(N'dbo.BoxQrCode',          N'U') IS NOT NULL DELETE FROM dbo.BoxQrCode;
IF OBJECT_ID(N'dbo.Manifest',           N'U') IS NOT NULL DELETE FROM dbo.Manifest;
IF OBJECT_ID(N'dbo.ConvoyRouteStop',    N'U') IS NOT NULL DELETE FROM dbo.ConvoyRouteStop;
IF OBJECT_ID(N'dbo.Box',                N'U') IS NOT NULL DELETE FROM dbo.Box;
IF OBJECT_ID(N'dbo.Vehicle',            N'U') IS NOT NULL DELETE FROM dbo.Vehicle;
IF OBJECT_ID(N'dbo.Convoy',             N'U') IS NOT NULL DELETE FROM dbo.Convoy;
IF OBJECT_ID(N'dbo.Person',             N'U') IS NOT NULL DELETE FROM dbo.Person;
IF OBJECT_ID(N'dbo.Receiver',           N'U') IS NOT NULL DELETE FROM dbo.Receiver;

COMMIT;
GO

-- Reseed the IDENTITY tables so a fresh run gets small, readable ids again. Guarded: RESEED
-- on a table that has had no inserts since CREATE makes the *next* insert land on 0, so only
-- reseed once a value has actually been consumed. DBCC is not transactional, hence its own batch.
IF OBJECT_ID(N'dbo.Convoy', N'U') IS NOT NULL AND IDENT_CURRENT(N'dbo.Convoy') > 1
    DBCC CHECKIDENT (N'dbo.Convoy', RESEED, 0);
IF OBJECT_ID(N'dbo.Box', N'U') IS NOT NULL AND IDENT_CURRENT(N'dbo.Box') > 1
    DBCC CHECKIDENT (N'dbo.Box', RESEED, 0);
IF OBJECT_ID(N'sensitive.ReceiverDetailAccessLog', N'U') IS NOT NULL AND IDENT_CURRENT(N'sensitive.ReceiverDetailAccessLog') > 1
    DBCC CHECKIDENT (N'sensitive.ReceiverDetailAccessLog', RESEED, 0);
GO

PRINT 'Freedom data reset complete.';
GO
