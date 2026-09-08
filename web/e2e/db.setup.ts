import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

import { test as setup } from '@playwright/test';

import { MARK_FILE, captureDbMark } from './db';
import { stackIsUp } from './stack';

// Records a database timestamp before the smokes run. global.teardown.ts deletes everything
// created at or after it, so the run cleans up after itself without disturbing rows a developer
// made by hand earlier. Best effort: if the stack or Docker is unavailable, no marker is
// written and teardown does nothing.
setup('capture a database marker for teardown', async () => {
  if (!(await stackIsUp())) {
    setup.skip(true, 'the local stack is not up (docker compose + tofu apply)');
    return;
  }

  try {
    const mark = captureDbMark();
    mkdirSync(dirname(MARK_FILE), { recursive: true });
    writeFileSync(MARK_FILE, mark, 'utf8');
  } catch (error) {
    console.warn(
      '[e2e setup] could not capture a database marker; teardown cleanup disabled:',
      error,
    );
  }
});
