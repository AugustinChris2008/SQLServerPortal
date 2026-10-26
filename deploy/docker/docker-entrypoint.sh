#!/bin/sh
set -eu

mode="${DataSource__Mode:-Demo}"
principal="${SQLPORTAL_KERBEROS_PRINCIPAL:-}"
keytab="${SQLPORTAL_KERBEROS_KEYTAB:-/run/secrets/sqlportal.keytab}"
password_file="${SQLPORTAL_KERBEROS_PASSWORD_FILE:-}"

if [ "$(printf '%s' "$mode" | tr '[:upper:]' '[:lower:]')" = "sqlserver" ]; then
    if [ -n "$principal" ] && [ -r "$keytab" ]; then
        kinit -k -t "$keytab" "$principal"
    elif [ -n "$principal" ] && [ -n "$password_file" ] && [ -r "$password_file" ]; then
        kinit "$principal" < "$password_file"
    elif ! klist -s; then
        echo "SQL Server mode requires a Kerberos ticket. Mount a keytab or ticket cache." >&2
        exit 1
    fi
fi

exec dotnet SqlPortal.Api.dll
