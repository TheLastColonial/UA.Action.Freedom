import { expect, test, vi } from 'vitest';

import { renderWithProviders } from '../../test/render';
import { VehicleServicingForm } from './VehicleServicingForm';

function renderForm(overrides: Partial<Parameters<typeof VehicleServicingForm>[0]> = {}) {
  const onSubmit = vi.fn();
  const screen = renderWithProviders(
    <VehicleServicingForm
      vin="VIN-TEST"
      initialStatus="Pending"
      initialNotes="Previous inspection notes"
      submitting={false}
      onSubmit={onSubmit}
      {...overrides}
    />,
  );
  return { screen, onSubmit };
}

test('starts from the recorded status and notes', async () => {
  const { screen } = renderForm({ initialStatus: 'Inspecting', initialNotes: 'Oil leak' });

  await expect
    .element((await screen).getByLabelText('Inspection status'))
    .toHaveValue('Inspecting');
  await expect.element((await screen).getByLabelText(/Inspection notes/)).toHaveValue('Oil leak');
});

test('submits the chosen status with trimmed notes', async () => {
  const { screen: rendered, onSubmit } = renderForm();
  const screen = await rendered;

  await screen.getByLabelText('Inspection status').selectOptions('Passed');
  await screen.getByLabelText(/Inspection notes/).fill('  New inspection findings  ');
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await vi.waitFor(() => {
    expect(onSubmit).toHaveBeenCalledWith({ status: 'Passed', notes: 'New inspection findings' });
  });
});

test('submits no notes when the field is blank', async () => {
  const { screen: rendered, onSubmit } = renderForm({ initialNotes: null });
  const screen = await rendered;

  await screen.getByLabelText('Inspection notes (defects/issues)').fill('   ');
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await vi.waitFor(() => {
    expect(onSubmit).toHaveBeenCalledWith({ status: 'Pending' });
  });
});

test('shows the error it is given', async () => {
  const { screen } = renderForm({ errorMessage: 'Something went wrong' });

  await expect.element((await screen).getByRole('alert')).toHaveTextContent('Something went wrong');
});
