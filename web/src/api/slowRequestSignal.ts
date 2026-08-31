type Listener = () => void;

const listeners = new Set<Listener>();

/** Subscribe to "a request has outlived the slow threshold" (cold start). Returns an unsubscribe. */
export function onSlowRequest(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function emitSlowRequest(): void {
  for (const listener of listeners) {
    listener();
  }
}
