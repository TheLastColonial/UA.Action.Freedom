import { expect, test, vi } from 'vitest';

import { renderWithProviders } from '../test/render';
import { TabPanel, Tabs } from './Tabs';

test('renders each tab with role="tab"', async () => {
  const onChange = vi.fn();
  const screen = await renderWithProviders(
    <Tabs
      label="Test tabs"
      tabs={[
        { id: 'a', label: 'Tab A' },
        { id: 'b', label: 'Tab B' },
      ]}
      active="a"
      onChange={onChange}
    />,
  );

  await expect.element(screen.getByRole('tab', { name: 'Tab A' })).toBeInTheDocument();
  await expect.element(screen.getByRole('tab', { name: 'Tab B' })).toBeInTheDocument();
});

test('the active tab has aria-selected="true" and others have aria-selected="false"', async () => {
  const onChange = vi.fn();
  const screen = await renderWithProviders(
    <Tabs
      label="Test tabs"
      tabs={[
        { id: 'a', label: 'Tab A' },
        { id: 'b', label: 'Tab B' },
      ]}
      active="a"
      onChange={onChange}
    />,
  );

  const tabA = screen.getByRole('tab', { name: 'Tab A' });
  const tabB = screen.getByRole('tab', { name: 'Tab B' });

  await expect.element(tabA).toHaveAttribute('aria-selected', 'true');
  await expect.element(tabB).toHaveAttribute('aria-selected', 'false');
});

test('clicking a tab calls onChange with its id', async () => {
  const onChange = vi.fn();
  const screen = await renderWithProviders(
    <Tabs
      label="Test tabs"
      tabs={[
        { id: 'a', label: 'Tab A' },
        { id: 'b', label: 'Tab B' },
      ]}
      active="a"
      onChange={onChange}
    />,
  );

  await screen.getByRole('tab', { name: 'Tab B' }).click();

  expect(onChange).toHaveBeenCalledWith('b');
});

test('TabPanel renders with correct ARIA attributes', async () => {
  const screen = await renderWithProviders(<TabPanel id="test-panel">Panel content</TabPanel>);

  const panel = screen.getByRole('tabpanel');
  await expect.element(panel).toHaveAttribute('id', 'tabpanel-test-panel');
  await expect.element(panel).toHaveAttribute('aria-labelledby', 'tab-test-panel');
  await expect.element(screen.getByText('Panel content')).toBeInTheDocument();
});

test('the active tab has tabIndex={0} and others have tabIndex={-1}', async () => {
  const onChange = vi.fn();
  const screen = await renderWithProviders(
    <Tabs
      label="Test tabs"
      tabs={[
        { id: 'a', label: 'Tab A' },
        { id: 'b', label: 'Tab B' },
      ]}
      active="a"
      onChange={onChange}
    />,
  );

  const tabA = screen.getByRole('tab', { name: 'Tab A' });
  const tabB = screen.getByRole('tab', { name: 'Tab B' });

  await expect.element(tabA).toHaveAttribute('tabindex', '0');
  await expect.element(tabB).toHaveAttribute('tabindex', '-1');
});
