import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router-dom';

import { useCreateDonation, useDonor } from '../../api/donations';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Button } from '../../components/Button';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { FormCard } from '../../components/form/FormCard';
import { TextField, TextareaField } from '../../components/form/fields';
import { donationFormSchema, donationFormToRequest, emptyDonationForm } from './donorFormModel';
import type { DonationFormValues } from './donorFormModel';

export function DonationCreatePage(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const donor = useDonor(id);
  const create = useCreateDonation();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<DonationFormValues>({
    resolver: zodResolver(donationFormSchema),
    defaultValues: emptyDonationForm(),
  });

  if (donor.isError && donor.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (donor.isPending) {
    return <PageSkeleton />;
  }
  if (donor.isError) {
    return <p role="alert">The donor could not be loaded.</p>;
  }

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  return (
    <section>
      <h1>Record a donation from {donor.data.name}</h1>
      <form
        noValidate
        onSubmit={(event) => {
          void handleSubmit((values) => {
            create.mutate(donationFormToRequest(id, values), {
              onSuccess: () => {
                void navigate(`/donors/${encodeURIComponent(id)}`);
              },
            });
          })(event);
        }}
      >
        {errorMessage ? (
          <p role="alert" className="field__error">
            {errorMessage}
          </p>
        ) : null}

        <FormCard title="Donation">
          <TextField
            label="Received on"
            type="date"
            error={errors.receivedOn?.message}
            {...register('receivedOn')}
          />
          <TextareaField
            label="Notes"
            hint="What was dropped off, or anything the donor said about it."
            error={errors.notes?.message}
            {...register('notes')}
          />
        </FormCard>

        <Button type="submit" disabled={create.isPending}>
          {create.isPending ? 'Saving…' : 'Record donation'}
        </Button>
      </form>
    </section>
  );
}
