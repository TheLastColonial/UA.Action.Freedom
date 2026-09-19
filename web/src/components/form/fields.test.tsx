import { render } from 'vitest-browser-react';
import { expect, test } from 'vitest';

import { SelectField, TextField, TextareaField } from './fields';

test('a text field is labelled and announces its error', async () => {
  const screen = await render(<TextField label="Plate" error="Plate is required" />);

  const input = screen.getByLabelText('Plate');
  await expect.element(input).toHaveAttribute('aria-invalid', 'true');
  await expect.element(input).toHaveAccessibleDescription('Plate is required');
  await expect.element(screen.getByRole('alert')).toHaveTextContent('Plate is required');
});

test('a field with no error is not marked invalid', async () => {
  const screen = await render(<TextField label="Plate" hint="As on the V5C" />);

  const input = screen.getByLabelText('Plate');
  await expect.element(input).not.toHaveAttribute('aria-invalid');
  await expect.element(input).toHaveAccessibleDescription('As on the V5C');
});

test('a select field offers its options under its label', async () => {
  const screen = await render(
    <SelectField
      label="Fuel"
      options={[
        { value: 'Diesel', label: 'Diesel' },
        { value: 'Petrol', label: 'Petrol' },
      ]}
    />,
  );

  await screen.getByLabelText('Fuel').selectOptions('Petrol');

  await expect.element(screen.getByLabelText('Fuel')).toHaveValue('Petrol');
});

test('a textarea field is labelled, takes text and announces its error', async () => {
  const screen = await render(
    <TextareaField label="Inspection notes" error="Notes must not exceed 2000 characters" />,
  );

  const textarea = screen.getByLabelText('Inspection notes');
  await textarea.fill('Clutch slipping');

  await expect.element(textarea).toHaveValue('Clutch slipping');
  await expect.element(textarea).toHaveAttribute('aria-invalid', 'true');
  await expect
    .element(textarea)
    .toHaveAccessibleDescription('Notes must not exceed 2000 characters');
});
