#!/bin/sh
# Loads the dev seed data into the database, then links each seed login to its volunteer.
#
# This is a separate step from schema deployment, run only when explicitly invoked
# via the db-seed compose service. Safe to run multiple times — the seed script guards
# against re-seeding an already-populated database (see database/seed/dev-seed.sql), and
# the link step only fills in volunteers whose login is not yet linked.
#
# The link step asks Keycloak for each seed login's subject, so `tofu apply` must have
# created the realm first. If Keycloak or a seed login is missing it warns and carries on:
# the volunteers are still seeded, and the links can be made by hand.
#
# Environment:
#   FREEDOM_DB_SERVER        target server              (default: mssql)
#   FREEDOM_DB_NAME          target database            (default: Freedom)
#   FREEDOM_DB_USER          connecting login           (default: sa)
#   FREEDOM_DB_PASSWORD      its password               (required)
#   FREEDOM_KEYCLOAK_URL     Keycloak base URL          (default: http://keycloak:8080)
#   FREEDOM_KEYCLOAK_REALM   the Freedom realm          (default: freedom)
#   KEYCLOAK_ADMIN           master-realm admin user    (default: admin)
#   KEYCLOAK_ADMIN_PASSWORD  its password               (default: admin)
set -eu

server="${FREEDOM_DB_SERVER:-mssql}"
database="${FREEDOM_DB_NAME:-Freedom}"
user="${FREEDOM_DB_USER:-sa}"
: "${FREEDOM_DB_PASSWORD:?FREEDOM_DB_PASSWORD is required}"
keycloak="${FREEDOM_KEYCLOAK_URL:-http://keycloak:8080}"
realm="${FREEDOM_KEYCLOAK_REALM:-freedom}"
admin_user="${KEYCLOAK_ADMIN:-admin}"
admin_password="${KEYCLOAK_ADMIN_PASSWORD:-admin}"

sql() {
    SQLCMDPASSWORD="$FREEDOM_DB_PASSWORD" sqlcmd \
        -S "$server" -U "$user" -d "$database" -C -b -I "$@"
}

keycloak_token() {
    curl -fsS -X POST "$keycloak/realms/master/protocol/openid-connect/token" \
        -d grant_type=password -d client_id=admin-cli \
        -d "username=$admin_user" --data-urlencode "password=$admin_password" \
        2>/dev/null | grep -o '"access_token":"[^"]*"' | cut -d'"' -f4 || true
}

login_subject() {
    curl -fsS -H "Authorization: Bearer $1" \
        "$keycloak/admin/realms/$realm/users?username=$2&exact=true" 2>/dev/null \
        | grep -o '"id":"[^"]*"' | head -n 1 | cut -d'"' -f4 || true
}

link_login() {
    subject=$(login_subject "$token" "$1")
    if ! printf '%s' "$subject" | grep -Eq '^[0-9a-f-]{36}$'; then
        echo "Not linked: no Keycloak login '$1' in realm '$realm' (run tofu apply first)."
        return 0
    fi
    sql -Q "IF NOT EXISTS (SELECT 1 FROM dbo.PersonDetail WHERE IdentitySubject = '$subject')
                UPDATE dbo.PersonDetail SET IdentitySubject = '$subject'
                WHERE FirstName = N'$2' AND LastName = N'$3' AND IdentitySubject IS NULL;
            IF @@ROWCOUNT = 0
                PRINT N'Not linked: no unlinked volunteer $2 $3 (already linked, or not seeded).';"
}

echo "Loading dev seed into $database on $server."
sql -i /deploy/seed/dev-seed.sql

token=$(keycloak_token)
if [ -z "$token" ]; then
    echo "Not linking seed logins: could not get a Keycloak admin token from $keycloak."
    exit 0
fi

link_login admin Ada Admin
link_login operator Olly Operator
link_login groundofficer Gus Ground
link_login loader Lou Loader
link_login leader Lee Leader
