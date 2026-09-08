import { execFileSync } from 'node:child_process';

/**
 * Talks to the local SQL Server container so the Playwright teardown can undo what a smoke run
 * created. Best-effort helpers — callers wrap them in try/catch; a missing Docker daemon or a
 * remote target simply means no cleanup, matching the self-skip contract the smokes follow.
 *
 * The BDD suite does the equivalent from C# (tests/.../Support/DataResetHook.cs). Both scope
 * the delete to "created since a marker time" so a developer's hand-made rows survive.
 */

const container = process.env['FREEDOM_DB_CONTAINER'] ?? 'freedom-mssql';

/** File the marker time is written to by db.setup.ts and read back by global.teardown.ts. */
export const MARK_FILE = 'e2e/.db-mark';

function sqlcmd(args: string): string {
  // $MSSQL_SA_PASSWORD is expanded inside the container (it is in the mssql service env), so it
  // never lands on this process's argv. `sa` is the only login allowed to touch sensitive.*.
  const out: string = execFileSync(
    'docker',
    [
      'exec',
      container,
      '/bin/sh',
      '-c',
      `/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b ${args}`,
    ],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
  );
  return out;
}

/** A database-side timestamp to bound the teardown cleanup by. */
export function captureDbMark(): string {
  const out = sqlcmd(
    '-h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(33), SYSUTCDATETIME(), 126)"',
  );
  return (
    out
      .split(/\r?\n/)
      .map((line) => line.trim())
      .find((line) => line.length > 0) ?? ''
  );
}

/** Deletes every row created at or after {@link mark}. Runs iac/local/sql/003-clean-since.sql. */
export function cleanSince(mark: string): void {
  sqlcmd(`-v Mark="${mark}" -i /sql/003-clean-since.sql`);
}
