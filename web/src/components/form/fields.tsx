import type { ComponentPropsWithRef, JSX, ReactNode } from 'react';
import { useId } from 'react';

import './fields.css';

interface FieldShellProps {
  label: string;
  error?: string | undefined;
  hint?: string | undefined;
  children: (ids: { inputId: string; describedBy: string | undefined }) => ReactNode;
}

function FieldShell({ label, error, hint, children }: FieldShellProps): JSX.Element {
  const inputId = useId();
  const errorId = `${inputId}-error`;
  const hintId = `${inputId}-hint`;
  const describedBy = [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ');

  return (
    <div className="field">
      <label className="field__label" htmlFor={inputId}>
        {label}
      </label>
      {hint ? (
        <p id={hintId} className="field__hint">
          {hint}
        </p>
      ) : null}
      {children({ inputId, describedBy: describedBy.length > 0 ? describedBy : undefined })}
      {error ? (
        <p id={errorId} className="field__error" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

type InputProps = Omit<ComponentPropsWithRef<'input'>, 'id'>;

export function TextField({
  label,
  error,
  hint,
  ...input
}: {
  label: string;
  error?: string | undefined;
  hint?: string | undefined;
} & InputProps): JSX.Element {
  return (
    <FieldShell label={label} error={error} hint={hint}>
      {({ inputId, describedBy }) => (
        <input
          id={inputId}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          {...input}
        />
      )}
    </FieldShell>
  );
}

export function CheckboxField({
  label,
  error,
  ...input
}: { label: string; error?: string | undefined } & InputProps): JSX.Element {
  const inputId = useId();
  const errorId = `${inputId}-error`;
  return (
    <div className="field field--inline">
      <input
        id={inputId}
        type="checkbox"
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        {...input}
      />
      <label htmlFor={inputId}>{label}</label>
      {error ? (
        <p id={errorId} className="field__error" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

type SelectProps = Omit<ComponentPropsWithRef<'select'>, 'id'>;

export function SelectField({
  label,
  error,
  options,
  ...select
}: {
  label: string;
  error?: string | undefined;
  options: readonly { value: string; label: string }[];
} & SelectProps): JSX.Element {
  return (
    <FieldShell label={label} error={error}>
      {({ inputId, describedBy }) => (
        <select
          id={inputId}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          {...select}
        >
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      )}
    </FieldShell>
  );
}
