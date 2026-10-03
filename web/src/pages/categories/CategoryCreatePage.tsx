import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreateCategory } from '../../api/categories';
import { ApiDomainProblem } from '../../api/problem';
import { CategoryForm } from './CategoryForm';
import { categoryFormToRequest, emptyCategoryForm } from './categoryModels';
import type { CategoryFormValues } from './categoryModels';

export function CategoryCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateCategory();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  const submit = (values: CategoryFormValues) => {
    create.mutate(categoryFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/categories/${created.id}/edit`);
      },
    });
  };

  return (
    <section>
      <h1>New category</h1>
      <CategoryForm
        initialValues={emptyCategoryForm()}
        submitLabel="Create category"
        submitting={create.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
