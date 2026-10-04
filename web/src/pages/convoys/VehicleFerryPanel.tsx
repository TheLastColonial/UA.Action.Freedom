import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { useCancelFerryBooking, useFerryBooking, useRecordFerryBooking } from '../../api/convoys';
import type { FerryBookingReadModel, RecordFerryBookingRequest } from '../../api/schemas/convoys';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { TextField } from '../../components/form/fields';
import { Spinner } from '../../components/Spinner';

const ferryFormSchema = z.object({
  operator: z.string().trim().min(1, 'Operator is required').max(200),
  reference: z.string().trim().min(1, 'Booking reference is required').max(100),
  sailingAt: z.string().min(1, 'Sailing is required'),
  ticketDetails: z.string().max(1000, 'Ticket details must be 1000 characters or fewer'),
  costGbp: z
    .string()
    .refine((value) => value.trim() === '' || Number(value) >= 0, 'Cost cannot be negative'),
});

type FerryFormValues = z.infer<typeof ferryFormSchema>;

// <input type="datetime-local"> works in minutes; the booking is read and written as UTC.
const toLocalInput = (iso: string) => iso.slice(0, 16);

function toRequest(values: FerryFormValues): RecordFerryBookingRequest {
  const request: RecordFerryBookingRequest = {
    operator: values.operator.trim(),
    reference: values.reference.trim(),
    sailingAt: `${values.sailingAt}:00Z`,
  };
  const details = values.ticketDetails.trim();
  if (details !== '') request.ticketDetails = details;
  if (values.costGbp.trim() !== '') request.costGbp = Number(values.costGbp);
  return request;
}

function toFormValues(booking: FerryBookingReadModel | null): FerryFormValues {
  return {
    operator: booking?.operator ?? '',
    reference: booking?.reference ?? '',
    sailingAt: booking ? toLocalInput(booking.sailingAt) : '',
    ticketDetails: booking?.ticketDetails ?? '',
    costGbp: booking?.costGbp === null || booking === null ? '' : String(booking.costGbp),
  };
}

interface VehicleFerryPanelProps {
  convoyId: number;
  vin: string;
  plate: string;
  withdrawn: boolean;
}

/**
 * A vehicle's outbound ferry booking (P1): one per vehicle, with a reference and ticket details.
 * There is no return crossing to book, because vehicles are handed over in Ukraine.
 */
export function VehicleFerryPanel({
  convoyId,
  vin,
  plate,
  withdrawn,
}: VehicleFerryPanelProps): JSX.Element {
  const query = useFerryBooking(convoyId, vin);
  const cancel = useCancelFerryBooking(convoyId, vin);

  if (query.isPending) {
    return <Spinner label="Loading ferry booking…" />;
  }
  if (query.isError) {
    return <p role="alert">The ferry booking for {plate} could not be loaded.</p>;
  }

  const booking = query.data;

  return (
    <div>
      {booking === null ? (
        <p role="status">Ferry not booked</p>
      ) : (
        <p role="status">
          Booked with {booking.operator}, reference {booking.reference}, sailing{' '}
          {toLocalInput(booking.sailingAt).replace('T', ' ')}.
        </p>
      )}
      {withdrawn ? null : (
        <Gate policy="convoys:write">
          <FerryForm
            convoyId={convoyId}
            vin={vin}
            plate={plate}
            initialValues={toFormValues(booking)}
          />
          {booking === null ? null : (
            <Button
              type="button"
              variant="danger"
              disabled={cancel.isPending}
              onClick={() => {
                cancel.mutate();
              }}
            >
              Cancel booking
            </Button>
          )}
        </Gate>
      )}
    </div>
  );
}

function FerryForm({
  convoyId,
  vin,
  plate,
  initialValues,
}: Omit<VehicleFerryPanelProps, 'withdrawn'> & { initialValues: FerryFormValues }): JSX.Element {
  const record = useRecordFerryBooking(convoyId, vin);
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FerryFormValues>({
    resolver: zodResolver(ferryFormSchema),
    defaultValues: initialValues,
  });

  return (
    <form
      noValidate
      aria-label={`Ferry for ${plate}`}
      onSubmit={(event) => {
        void handleSubmit((values) => {
          record.mutate(toRequest(values));
        })(event);
      }}
    >
      <TextField
        label="Ferry operator"
        error={errors.operator?.message}
        {...register('operator')}
      />
      <TextField
        label="Booking reference"
        error={errors.reference?.message}
        {...register('reference')}
      />
      <TextField
        label="Sailing"
        type="datetime-local"
        error={errors.sailingAt?.message}
        {...register('sailingAt')}
      />
      <TextField
        label="Ticket details"
        error={errors.ticketDetails?.message}
        {...register('ticketDetails')}
      />
      <TextField
        label="Cost (£)"
        inputMode="decimal"
        error={errors.costGbp?.message}
        {...register('costGbp')}
      />
      {record.isError ? (
        <p role="alert" className="field__error">
          {record.error.message}
        </p>
      ) : null}
      <Button type="submit" disabled={record.isPending}>
        {record.isPending ? 'Saving…' : 'Book ferry'}
      </Button>
    </form>
  );
}
