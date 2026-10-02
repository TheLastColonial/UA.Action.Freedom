import type { JSX } from 'react';
import { useState } from 'react';

import { useMe } from '../../api/me';
import { useLinkLogin } from '../../api/people';
import { ApiDomainProblem } from '../../api/problem';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { TextField } from '../../components/form/fields';

interface LinkLoginPanelProps {
  personId: string;
}

/**
 * An Administrator links the login a volunteer signs in with. The login is identified by its
 * token subject; "Use my login" fills in the Administrator's own, which is how the first link
 * (their own) gets made.
 */
export function LinkLoginPanel({ personId }: LinkLoginPanelProps): JSX.Element {
  return (
    <Gate policy="people:write">
      <LinkLoginForm personId={personId} />
    </Gate>
  );
}

function LinkLoginForm({ personId }: LinkLoginPanelProps): JSX.Element {
  const me = useMe();
  const link = useLinkLogin(personId);
  const [subject, setSubject] = useState('');

  const message = link.isError
    ? link.error instanceof ApiDomainProblem
      ? (link.error.detail ?? link.error.message)
      : 'The login could not be linked.'
    : undefined;

  return (
    <section aria-labelledby="link-login-heading">
      <h2 id="link-login-heading">Link login</h2>
      <p>
        Connects this volunteer to the login they sign in with, so what they do is recorded in their
        name.
      </p>
      {link.isSuccess ? <p role="status">Login linked.</p> : null}
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}
      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          link.mutate({ subject: subject.trim() });
        }}
      >
        <TextField
          label="Login subject"
          value={subject}
          onChange={(event) => {
            setSubject(event.target.value);
          }}
        />
        <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
          <Button type="submit" disabled={link.isPending || subject.trim().length === 0}>
            Link login
          </Button>
          <Button
            type="button"
            variant="secondary"
            disabled={!me.isSuccess || me.data.subject === null}
            onClick={() => {
              setSubject(me.data?.subject ?? '');
            }}
          >
            Use my login
          </Button>
        </span>
      </form>
    </section>
  );
}
