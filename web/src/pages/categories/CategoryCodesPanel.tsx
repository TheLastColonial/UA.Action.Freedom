import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { useSetCategoryCode } from '../../api/categories';
import { ApiDomainProblem } from '../../api/problem';
import { CUSTOMS_AUTHORITIES } from '../../api/schemas/categories';
import type { CustomsAuthority, ItemCategoryReadModel } from '../../api/schemas/categories';
import { Button } from '../../components/Button';
import { TextField } from '../../components/form/fields';
import { AUTHORITY_LABELS, codeFormSchema, codeOf, codeToRequestValue } from './categoryModels';

interface AuthorityCodeFormProps {
  category: ItemCategoryReadModel;
  authority: CustomsAuthority;
}

function AuthorityCodeForm({ category, authority }: AuthorityCodeFormProps): JSX.Element {
  const setCode = useSetCategoryCode(category.id);
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<{ code: string }>({
    resolver: zodResolver(codeFormSchema),
    defaultValues: { code: codeOf(category, authority) },
  });

  const errorMessage =
    setCode.error instanceof ApiDomainProblem
      ? (setCode.error.detail ?? setCode.error.message)
      : undefined;

  return (
    <form
      noValidate
      onSubmit={(event) => {
        void handleSubmit(({ code }) => {
          setCode.mutate({ authority, code: codeToRequestValue(code) });
        })(event);
      }}
    >
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}
      <TextField
        label={`${AUTHORITY_LABELS[authority]} code`}
        inputMode="numeric"
        error={errors.code?.message}
        {...register('code')}
      />
      <Button type="submit" variant="secondary" disabled={setCode.isPending}>
        {`Save ${authority} code`}
      </Button>
    </form>
  );
}

interface CategoryCodesPanelProps {
  category: ItemCategoryReadModel;
}

/**
 * The customs code this category is declared under, per authority. An item with no code of its own is
 * declared under the code of its category for the authority being filed with (ADR 0014). Clearing a
 * field removes the mapping.
 */
export function CategoryCodesPanel({ category }: CategoryCodesPanelProps): JSX.Element {
  return (
    <section aria-labelledby="category-codes-heading">
      <h2 id="category-codes-heading">Customs codes</h2>
      {CUSTOMS_AUTHORITIES.map((authority) => (
        <AuthorityCodeForm
          key={`${authority}-${codeOf(category, authority)}`}
          category={category}
          authority={authority}
        />
      ))}
    </section>
  );
}
