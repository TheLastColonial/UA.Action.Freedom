const baseUrl = process.env['PLAYWRIGHT_BASE_URL'] ?? 'http://localhost:8080';

let checked = false;
let up = false;

/**
 * The smokes run against the deployed docker-compose stack (see iac/README.md). When it is
 * not up they self-skip, so `npm run e2e` is green with nothing running — the same contract
 * the .NET Integration and BDD suites follow.
 */
export async function stackIsUp(): Promise<boolean> {
  if (checked) {
    return up;
  }
  checked = true;
  try {
    const response = await fetch(`${baseUrl}/health/live`);
    up = response.ok;
  } catch {
    up = false;
  }
  return up;
}
