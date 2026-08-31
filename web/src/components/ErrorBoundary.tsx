import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
}

/** Last-resort boundary for render-time failures outside the router's own error handling. */
export class ErrorBoundary extends Component<Props, State> {
  override state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // No PII in here — component stacks only.
    console.error('Unhandled UI error', error.message, info.componentStack);
  }

  override render(): ReactNode {
    if (this.state.error) {
      return (
        <section role="alert">
          <h1>Something went wrong</h1>
          <p>The page could not be displayed. Reloading usually clears it.</p>
          <button
            type="button"
            onClick={() => {
              window.location.reload();
            }}
          >
            Reload
          </button>
        </section>
      );
    }
    return this.props.children;
  }
}
