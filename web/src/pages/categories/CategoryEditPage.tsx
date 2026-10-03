import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useCategory, useUpdateCategory } from '../../api/categories';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { CategoryCodesPanel } from './CategoryCodesPanel';
import { CategoryForm } from './CategoryForm';
import { categoryFormToUpdateRequest, categoryToFormValues } from './categoryModels';
import type { CategoryFormValues } from './categoryModels';

export function CategoryEditPage(): JSX.Element {
  const { id = '' } = useParams();
  const categoryId = Number(id);
  const navigate = useNavigate();
  const query = useCategory(categoryId);
  const update = useUpdateCategory(categoryId);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The category could not be loaded.</p>;
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;

  const submit = (values: CategoryFormValues) => {
    update.mutate(categoryFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate('/categories');
      },
    });
  };

  return (
    <section>
      <h1>Edit {query.data.nameEn}</h1>
      <CategoryForm
        initialValues={categoryToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
      <CategoryCodesPanel category={query.data} />
    </section>
  );
}
