/*
    In Azure the operator roles map to Entra groups holding the app roles from
    docs/domain/key-concepts.md, with Entra-only authentication on the server (4.2). Locally they
    are plain database roles. Either way the grants in Security/Permissions.sql are what the
    application code has to satisfy.

    Membership is not declared here: the environment layer adds its own principals to these roles
    (iac/local/sql/principals.sql locally, the deployment pipeline in Azure).
*/
CREATE ROLE [freedom_app] AUTHORIZATION [dbo];     -- the Freedom Application's own identity
GO
CREATE ROLE [freedom_worker] AUTHORIZATION [dbo];  -- the Customs Worker's own identity
GO
CREATE ROLE [administrator] AUTHORIZATION [dbo];
GO
CREATE ROLE [dispatcher] AUTHORIZATION [dbo];
GO
CREATE ROLE [loader] AUTHORIZATION [dbo];
GO
CREATE ROLE [purchaser] AUTHORIZATION [dbo];
GO
CREATE ROLE [ground_officer] AUTHORIZATION [dbo];
GO
