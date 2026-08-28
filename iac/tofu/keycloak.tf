# Identity, standing in for Microsoft Entra External ID.
#
# The thing worth reproducing locally is not Keycloak — it is the *role model*. The target
# design uses app roles rather than group membership, because group-based assignment to an
# enterprise application needs an Entra ID P1 licence and app roles do not
# (docs/recommendations.md 4.7). Roles arrive as claims in the token either way, so
# authorisation policies written against this realm port to Entra unchanged.

resource "keycloak_realm" "freedom" {
  realm        = var.realm_name
  enabled      = true
  display_name = "UA Action Freedom (local)"

  # Volunteers sign in with an email address and no Microsoft account, matching the email
  # OTP flow the target design uses.
  login_with_email_allowed = true
  registration_allowed     = false

  # Short-lived tokens are the compensating control for having no Conditional Access on
  # the free tier: a leaked token stops being useful quickly.
  access_token_lifespan    = "15m"
  sso_session_idle_timeout = "8h"
}

resource "keycloak_openid_client" "freedom_app" {
  realm_id  = keycloak_realm.freedom.id
  client_id = var.oidc_client_id
  name      = "Freedom Application"

  # Confidential, authorisation-code flow — the application is a server-side deployable
  # that can keep a secret, so there is no reason to use a public client.
  access_type   = "CONFIDENTIAL"
  client_secret = var.oidc_client_secret

  standard_flow_enabled = true

  # Enabled so that `curl`-driven checks and integration tests can obtain a token for a
  # seeded user without driving a browser. Would not be enabled against a real tenant.
  direct_access_grants_enabled = true

  valid_redirect_uris = [
    "${var.edge_url}/signin-oidc",
    "${var.edge_url}/*",
  ]
  valid_post_logout_redirect_uris = ["${var.edge_url}/*"]
  web_origins                     = [var.edge_url]
}

# One role per role in docs/domain/key-concepts.md. Least privilege is modelled explicitly
# rather than through a single "staff" role — a Loader confirms box contents and has no
# business seeing convoy routes or receiver detail.
locals {
  app_roles = {
    Administrator = "Manages accounts, roles and MFA policy."
    Dispatcher    = "Plans convoys, builds manifests, requests GMR and ELO."
    Loader        = "Confirms donation contents, weighs and validates boxes."
    Purchaser     = "Records sourced vehicles and supplies."
    GroundOfficer = "Coordinates with local authorities; the only role that may resolve receiver addresses."
  }

  # Three seeded logins, each a member of the groups that carry its roles:
  #   Admin        — account, role and MFA administration only.
  #   Operator     — the day-to-day operational roles, so one login walks that whole path.
  #   GroundOfficer — kept as its own login so the receiver-address segregation that this
  #                   role carries in production (docs/domain/key-concepts.md, Data
  #                   Sensitivity) is preserved locally: no other seed user can resolve a
  #                   receiver address.
  seed_users = {
    Admin         = ["Administrator"]
    Operator      = ["Dispatcher", "Loader", "Purchaser"]
    GroundOfficer = ["GroundOfficer"]
  }
}

resource "keycloak_role" "app_role" {
  for_each = local.app_roles

  realm_id    = keycloak_realm.freedom.id
  client_id   = keycloak_openid_client.freedom_app.id
  name        = each.key
  description = each.value
}

# Each role is delegated through a group of the same name: adding a user to the group grants
# the role. This is the group-membership assignment model Entra keeps behind a P1 licence;
# it is used here purely because it keeps the two seed logins readable.
resource "keycloak_group" "app_role" {
  for_each = local.app_roles

  realm_id = keycloak_realm.freedom.id
  name     = each.key
}

resource "keycloak_group_roles" "app_role" {
  for_each = local.app_roles

  realm_id = keycloak_realm.freedom.id
  group_id = keycloak_group.app_role[each.key].id
  role_ids = [keycloak_role.app_role[each.key].id]
}

# Put the roles in the token as a flat `roles` claim, which is the shape an ASP.NET Core
# authorisation policy expects and the shape Entra app roles arrive in.
resource "keycloak_openid_user_client_role_protocol_mapper" "roles" {
  realm_id   = keycloak_realm.freedom.id
  client_id  = keycloak_openid_client.freedom_app.id
  name       = "app-roles"
  claim_name = "roles"

  client_id_for_role_mappings = keycloak_openid_client.freedom_app.client_id
  multivalued                 = true
  add_to_access_token         = true
  add_to_id_token             = true
  add_to_userinfo             = true
}

# Two users, named for what they can do rather than for a person: these are fixtures, and
# seeding them with realistic volunteer names would put invented personal data in version
# control for no benefit.
resource "keycloak_user" "seed" {
  for_each = local.seed_users

  realm_id   = keycloak_realm.freedom.id
  username   = lower(each.key)
  email      = "${lower(each.key)}@local.test"
  first_name = each.key
  # Keycloak validates names against a person-name pattern that rejects brackets and most
  # punctuation, so this cannot be decorated with "(local)" or similar.
  last_name = "Local"

  enabled        = true
  email_verified = true

  initial_password {
    value     = var.test_user_password
    temporary = false
  }
}

# Group membership is what grants roles here. Authoritative for each seed user, so a role
# removed from the list above is removed from the user on the next apply.
resource "keycloak_user_groups" "seed" {
  for_each = local.seed_users

  realm_id  = keycloak_realm.freedom.id
  user_id   = keycloak_user.seed[each.key].id
  group_ids = [for role in each.value : keycloak_group.app_role[role].id]
}
