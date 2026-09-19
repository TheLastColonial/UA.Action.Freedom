/*
    Who connects to the local Freedom database, and as which role.

    The schema, the roles and what each role may do (including the DENY on the sensitive schema)
    are the dacpac's — database/UA.Action.Freedom.Database, published by the db-deploy compose
    service. This file is only the environment's half: the principals that exist here and not in
    Azure. In Azure both identities are managed identities with Entra-only authentication and no
    password (4.2), added with CREATE USER ... FROM EXTERNAL PROVIDER by the deployment pipeline
    instead of this file; the roles they join are the same.

    Why two logins rather than sa: sa is sysadmin, which bypasses permission checks entirely, so
    the DENY that recommendations 4.4 calls load-bearing would be decorative.

      freedom_app        the application's own identity. Full DML on dbo, DENY on sensitive.
      freedom_sensitive  the Ground Officer path, and the only way to read a delivery address.

    Passwords arrive as sqlcmd scripting variables resolved from the environment, so they are
    never on a command line — see ../../tofu/database.tf. User names differ from role names
    because a database principal cannot share a name with a role in the same database.

    Guarded by IF NOT EXISTS because `tofu apply` may re-run against a database that already has
    them; this is environment configuration, not schema, so it holds no migration logic.
*/

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'freedom_app')
    CREATE LOGIN freedom_app WITH PASSWORD = '$(FREEDOM_APP_PASSWORD)', CHECK_POLICY = OFF;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'freedom_sensitive')
    CREATE LOGIN freedom_sensitive WITH PASSWORD = '$(FREEDOM_SENSITIVE_PASSWORD)', CHECK_POLICY = OFF;
GO

USE Freedom;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'freedom_app_user')
    CREATE USER freedom_app_user FOR LOGIN freedom_app;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'freedom_sensitive_user')
    CREATE USER freedom_sensitive_user FOR LOGIN freedom_sensitive;
GO

IF IS_ROLEMEMBER('freedom_app', 'freedom_app_user') = 0
    ALTER ROLE freedom_app ADD MEMBER freedom_app_user;
GO

IF IS_ROLEMEMBER('ground_officer', 'freedom_sensitive_user') = 0
    ALTER ROLE ground_officer ADD MEMBER freedom_sensitive_user;
GO

PRINT 'Freedom principals in place.';
GO
