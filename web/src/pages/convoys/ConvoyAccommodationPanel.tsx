import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import {
  useAccommodation,
  useAccommodationCoverage,
  useBookAccommodation,
  useSetSelfAccommodation,
} from '../../api/accommodation';
import { ApiDomainProblem } from '../../api/problem';
import type { AccommodationCoverage, CoverageStatus } from '../../api/schemas/accommodation';
import { Button } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { CheckboxField, SelectField, TextField } from '../../components/form/fields';
import { PageSkeleton } from '../../components/PageSkeleton';

const STATUS_TEXT: Record<CoverageStatus, string> = {
  Booked: 'Booked',
  SelfArranged: 'Arranging their own',
  Missing: 'Missing',
};

const bookingFormSchema = z
  .object({
    routePointId: z.string().min(1, 'Choose a stop'),
    provider: z.string().trim().min(1, 'Enter the provider').max(200, 'Provider is too long'),
    checkIn: z.string().min(1, 'Enter the check in date'),
    checkOut: z.string().min(1, 'Enter the check out date'),
    costGbp: z
      .string()
      .refine((value) => value.trim() === '' || Number(value) >= 0, 'Cost cannot be negative'),
    reference: z.string().max(100, 'Reference is too long'),
    details: z.string().max(1000, 'Details are too long'),
    guests: z.record(z.string(), z.boolean()),
  })
  .refine(
    (values) =>
      values.checkIn === '' || values.checkOut === '' || values.checkOut >= values.checkIn,
    {
      path: ['checkOut'],
      message: 'Check out cannot be before check in',
    },
  )
  .refine((values) => Object.values(values.guests).some(Boolean), {
    path: ['guests'],
    message: 'Choose at least one crew member',
  });
type BookingFormValues = z.infer<typeof bookingFormSchema>;

interface ConvoyAccommodationPanelProps {
  convoyId: number;
}

/**
 * A convoy's accommodation (P2, P8): a grid of overnight stops by crew member showing whether each is booked,
 * arranged by the person themselves or missing, the bookings behind it, and the forms to book a room (shared if
 * several people choose) or flag a night someone arranges themselves (O4, O30). Advice for now: plan 13 makes
 * coverage a requirement to depart.
 */
export function ConvoyAccommodationPanel({ convoyId }: ConvoyAccommodationPanelProps): JSX.Element {
  const coverage = useAccommodationCoverage(convoyId);
  const accommodation = useAccommodation(convoyId);

  if (coverage.isPending || accommodation.isPending) {
    return <PageSkeleton />;
  }
  if (coverage.isError || accommodation.isError) {
    return <p role="alert">The accommodation could not be loaded.</p>;
  }

  const stopName = (routePointId: number) =>
    coverage.data.stops.find((stop) => stop.routePointId === routePointId)?.name ??
    'a removed stop';
  const personName = (personId: string) =>
    coverage.data.crew.find((member) => member.personId === personId)?.name ?? 'Former crew member';

  return (
    <div>
      <DetailCard title="Who is covered each night">
        <CoverageGrid convoyId={convoyId} coverage={coverage.data} />
        {coverage.data.warnings.length > 0 ? (
          <ul role="alert">
            {coverage.data.warnings.map((warning) => (
              <li key={warning}>{warning}</li>
            ))}
          </ul>
        ) : null}
        <p className="field__hint">
          Every crew member needs a bed at every overnight stop, in a booking or by arranging their
          own. This is advice until departure checks it.
        </p>
      </DetailCard>

      <DetailCard title="Bookings">
        {accommodation.data.bookings.length === 0 ? (
          <p>No accommodation booked yet.</p>
        ) : (
          <ul>
            {accommodation.data.bookings.map((booking) => (
              <li key={booking.id}>
                {`${booking.provider} at ${stopName(booking.routePointId)}`}
                {`, ${booking.checkIn.slice(0, 10)} to ${booking.checkOut.slice(0, 10)}`}
                {`: ${booking.guests.map(personName).join(', ')}`}
                {booking.costGbp === null ? '' : `, £${booking.costGbp.toFixed(2)}`}
                {booking.cancelled ? ' (cancelled)' : ''}
              </li>
            ))}
          </ul>
        )}
        <Gate policy="convoys:write">
          {coverage.data.stops.length === 0 ? (
            <p className="field__hint">
              Flag an overnight stop on the route to book accommodation for it.
            </p>
          ) : (
            <BookingForm convoyId={convoyId} coverage={coverage.data} />
          )}
        </Gate>
      </DetailCard>
    </div>
  );
}

function CoverageGrid({
  convoyId,
  coverage,
}: {
  convoyId: number;
  coverage: AccommodationCoverage;
}): JSX.Element {
  const setSelf = useSetSelfAccommodation(convoyId);

  if (coverage.stops.length === 0 || coverage.crew.length === 0) {
    return (
      <p role="status">
        {coverage.stops.length === 0
          ? 'Every crew member is covered at every overnight stop.'
          : 'Nobody is crewed on this convoy yet.'}
      </p>
    );
  }

  const statusOf = (routePointId: number, personId: string): CoverageStatus =>
    coverage.cells.find((cell) => cell.routePointId === routePointId && cell.personId === personId)
      ?.status ?? 'Missing';

  return (
    <>
      <p role="status">
        {coverage.allCovered
          ? 'Every crew member is covered at every overnight stop.'
          : `${String(coverage.missingCount)} ${coverage.missingCount === 1 ? 'night is' : 'nights are'} not covered.`}
      </p>
      <table>
        <thead>
          <tr>
            <th scope="col">Crew</th>
            {coverage.stops.map((stop) => (
              <th key={stop.routePointId} scope="col">
                {stop.name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {coverage.crew.map((member) => (
            <tr key={member.personId}>
              <th scope="row">{member.name}</th>
              {coverage.stops.map((stop) => {
                const status = statusOf(stop.routePointId, member.personId);
                return (
                  <td
                    key={stop.routePointId}
                    aria-label={`${member.name} at ${stop.name}: ${STATUS_TEXT[status]}`}
                  >
                    {STATUS_TEXT[status]}
                    <Gate policy="convoys:write">
                      {status === 'Booked' ? null : (
                        <Button
                          type="button"
                          variant="secondary"
                          disabled={setSelf.isPending}
                          onClick={() => {
                            setSelf.mutate({
                              routePointId: stop.routePointId,
                              personId: member.personId,
                              arranged: status === 'Missing',
                            });
                          }}
                        >
                          {status === 'Missing'
                            ? `Mark ${member.name} as arranging their own at ${stop.name}`
                            : `Clear ${member.name} arranging their own at ${stop.name}`}
                        </Button>
                      )}
                    </Gate>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
      {setSelf.isError ? (
        <p role="alert" className="field__error">
          {setSelf.error instanceof ApiDomainProblem
            ? (setSelf.error.detail ?? setSelf.error.message)
            : setSelf.error.message}
        </p>
      ) : null}
    </>
  );
}

function BookingForm({
  convoyId,
  coverage,
}: {
  convoyId: number;
  coverage: AccommodationCoverage;
}): JSX.Element {
  const book = useBookAccommodation(convoyId);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<BookingFormValues>({
    resolver: zodResolver(bookingFormSchema),
    defaultValues: {
      routePointId: String(coverage.stops[0]?.routePointId ?? ''),
      provider: '',
      checkIn: '',
      checkOut: '',
      costGbp: '',
      reference: '',
      details: '',
      guests: {},
    },
  });

  const problem =
    book.error instanceof ApiDomainProblem ? (book.error.detail ?? book.error.message) : undefined;

  return (
    <form
      noValidate
      aria-label="Book accommodation"
      onSubmit={(event) => {
        void handleSubmit((values) => {
          book.mutate(
            {
              routePointId: Number(values.routePointId),
              provider: values.provider.trim(),
              checkIn: values.checkIn,
              checkOut: values.checkOut,
              guests: Object.entries(values.guests)
                .filter(([, chosen]) => chosen)
                .map(([personId]) => personId),
              ...(values.reference.trim() === '' ? {} : { reference: values.reference.trim() }),
              ...(values.details.trim() === '' ? {} : { details: values.details.trim() }),
              ...(values.costGbp.trim() === '' ? {} : { costGbp: Number(values.costGbp) }),
            },
            {
              onSuccess: () => {
                reset();
              },
            },
          );
        })(event);
      }}
    >
      <SelectField
        label="Stop"
        options={coverage.stops.map((stop) => ({
          value: String(stop.routePointId),
          label: stop.name,
        }))}
        {...register('routePointId')}
      />
      <TextField label="Provider" error={errors.provider?.message} {...register('provider')} />
      <TextField label="Reference" error={errors.reference?.message} {...register('reference')} />
      <TextField
        label="Check in"
        type="date"
        error={errors.checkIn?.message}
        {...register('checkIn')}
      />
      <TextField
        label="Check out"
        type="date"
        error={errors.checkOut?.message}
        {...register('checkOut')}
      />
      <TextField
        label="Cost (£)"
        inputMode="decimal"
        error={errors.costGbp?.message}
        {...register('costGbp')}
      />
      <TextField label="Details" error={errors.details?.message} {...register('details')} />
      <fieldset>
        <legend>Guests</legend>
        {coverage.crew.map((member) => (
          <CheckboxField
            key={member.personId}
            label={member.name}
            {...register(`guests.${member.personId}`)}
          />
        ))}
        {errors.guests?.['root']?.message ? (
          <p role="alert" className="field__error">
            {errors.guests['root'].message}
          </p>
        ) : null}
      </fieldset>
      {problem ? (
        <p role="alert" className="field__error">
          {problem}
        </p>
      ) : null}
      <Button type="submit" disabled={book.isPending}>
        {book.isPending ? 'Saving…' : 'Book accommodation'}
      </Button>
    </form>
  );
}
