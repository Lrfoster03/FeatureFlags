import { expect, takeSnapshot, test } from '@chromatic-com/playwright';
import { randomUUID } from 'node:crypto';

// Capture history states explicitly at their viewport size.
test.use({ timezoneId: 'America/Los_Angeles', disableAutoSnapshot: true, cropToViewport: true });

async function snapshotHistory(page, name, testInfo) {
  // Keep generated account details and server timestamps stable in visual baselines.
  await page.locator('.user-email, .history-description > strong:first-child').evaluateAll(elements => {
    elements.forEach(element => { element.textContent = 'audit@example.com'; });
  });
  await page.locator('.project-switcher button, .project-switcher .dropdown-item, .history-heading .small, .history-action > strong').evaluateAll(elements => {
    elements.forEach(element => {
      if (element.textContent.startsWith('Audit test project ')) element.textContent = 'Audit test project';
    });
  });
  await page.locator('dialog[open] time[data-audit-time]').evaluateAll(elements => {
    elements.forEach(element => { element.dateTime = '2026-07-01T19:00:00.000Z'; });
  });
  await page.evaluate(async () => {
    const { formatTimes } = await import('/Components/Shared/ProjectHistory.razor.js');
    formatTimes(document.querySelector('dialog[open]'));
  });
  await takeSnapshot(page, name, testInfo);
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`) });
}

test('project history shows saved diffs, preserves drafts, and stays within the selected project', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const testId = randomUUID();
  const email = `audit-${testId}@example.com`;
  const projectName = `Audit test project ${testId}`;
  const otherProjectName = `Separate project ${testId}`;
  await page.goto('/Identity/Account/Register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill('AuditTest1!');
  await page.getByLabel('Confirm password').fill('AuditTest1!');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page).toHaveURL(/\/projects$/);
  await expect(page.getByRole('button', { name: 'Project history', exact: true })).toHaveCount(0);
  await page.getByPlaceholder('New project name').fill(projectName);
  await page.getByRole('button', { name: 'Create project' }).click();
  await page.locator('a.list-group-item').filter({ hasText: projectName }).click();
  await expect(page.getByRole('heading', { name: 'Feature Flags' })).toBeVisible();
  const firstProjectUrl = page.url();
  const history = page.locator('.app-header').getByRole('button', { name: 'Project history', exact: true });
  await expect(history).toBeVisible();
  await expect(history).toBeEnabled();
  await page.getByRole('button', { name: 'Add Flag', exact: true }).click();
  const flag = page.locator('.feature-pill').first();
  await flag.locator('.pill-header').click();
  await flag.locator('.rollout-input').fill('25');
  await flag.locator('.rollout-input').press('Tab');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('You have unsaved changes.')).toHaveCount(0);
  await flag.locator('.pill-header').click();
  await flag.locator('.rollout-input').fill('80');
  await flag.locator('.rollout-input').press('Tab');
  await history.click();
  const modal = page.locator('dialog[open]');
  await expect(modal).toBeVisible();
  await expect(modal.locator('.history-row')).toHaveCount(3);
  await expect(modal.getByLabel('Who', { exact: true })).toContainText(email);
  await expect(modal.getByLabel('Environment', { exact: true })).toContainText('Development');
  await expect(modal.locator('.history-row').first()).toContainText('updated flag');
  await modal.locator('.history-row').first().click();
  await expect(modal.locator('.history-field')).toContainText('Rollout');
  await expect(modal.locator('.history-field')).toContainText('25%');
  await expect(modal.locator('.history-field')).not.toContainText('80%');
  await expect(modal.locator('[data-audit-clock]').first()).toContainText(/\d+:\d{2}:\d{2}.*P[SD]T/);
  await expect(modal).not.toContainText('America/Los_Angeles');
  await snapshotHistory(page, 'History desktop', testInfo);
  await modal.getByRole('button', { name: 'Close project history' }).focus();
  await page.keyboard.press('Shift+Tab');
  expect(await modal.evaluate(element => element.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Escape');
  await expect(modal).toHaveCount(0);
  await expect(history).toBeFocused();
  await expect(flag.locator('.rollout-input')).toHaveValue('80');
  await expect(page.getByText('You have unsaved changes.')).toBeVisible();
  await page.getByRole('button', { name: 'Discard', exact: true }).click();

  await page.getByRole('button', { name: 'Add Config', exact: true }).click();
  await expect(page.locator('.feature-pill')).toHaveCount(2);
  const config = page.locator('.feature-pill').last();
  await config.locator('.pill-header').click();
  const configText = '{"checkout":{"limit":5,"note":"<img src=x onerror=alert(1)>"},"nullable":null}';
  await config.locator('.config-value-input').fill(configText);
  await config.locator('.config-value-input').press('Tab');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('You have unsaved changes.')).toHaveCount(0);
  await config.locator('.pill-header').click();
  await config.getByRole('button', { name: 'Project history', exact: true }).click();
  await expect(modal.locator('.history-row')).toHaveCount(2);
  await modal.locator('.history-row').first().click();
  await expect(modal.locator('.history-field').filter({ hasText: 'limit' })).toContainText('Added 5');
  await expect(modal.locator('.history-field').filter({ hasText: 'nullable' })).toContainText('Added null');
  await expect(modal.locator('.history-detail img')).toHaveCount(0);
  await modal.getByRole('button', { name: 'Before / after', exact: true }).click();
  await expect(modal.locator('.history-json')).toContainText('checkout');
  await modal.getByRole('button', { name: 'Close project history' }).click();
  await expect(config.getByRole('button', { name: 'Project history', exact: true })).toBeFocused();

  await page.getByRole('link', { name: 'Settings', exact: true }).click();
  await history.click();
  await expect(modal.locator('.history-row')).toHaveCount(5);
  await modal.locator('summary').click();
  await modal.getByLabel('Type', { exact: true }).selectOption('member');
  await modal.getByRole('button', { name: 'Apply', exact: true }).click();
  await expect(modal.getByText('No changes match these filters.')).toBeVisible();
  await modal.getByRole('button', { name: 'Reset', exact: true }).click();
  await expect(modal.locator('.history-row')).toHaveCount(5);
  await modal.locator('summary').click();
  await page.setViewportSize({ width: 390, height: 844 });
  await modal.locator('.history-row').first().click();
  await expect(modal.locator('.history-row').first()).toHaveAttribute('aria-expanded', 'true');
  const box = await modal.boundingBox();
  expect(box.x).toBe(0);
  expect(box.y).toBe(0);
  expect(box.width).toBe(390);
  expect(box.height).toBe(844);
  expect(await modal.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  const actorBox = await modal.locator('.history-actor').first().boundingBox();
  const actionBox = await modal.locator('.history-action').first().boundingBox();
  expect(actionBox.y).toBeGreaterThanOrEqual(actorBox.y + actorBox.height);
  const field = modal.locator('.history-field').first();
  const labelBox = await field.locator('code').boundingBox();
  const valueBox = await field.locator(':scope > span').boundingBox();
  expect(valueBox.y).toBeGreaterThanOrEqual(labelBox.y + labelBox.height);
  const close = modal.getByRole('button', { name: 'Close project history' });
  expect((await close.boundingBox()).height).toBeGreaterThanOrEqual(44);
  await snapshotHistory(page, 'History mobile', testInfo);
  await modal.locator('.history-scroll').evaluate(element => { element.scrollTop = element.scrollHeight; });
  expect(await modal.locator('.history-scroll').evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  await expect(close).toBeInViewport();
  await close.click();
  await page.getByRole('button', { name: 'Toggle navigation' }).click();
  await history.click();
  await modal.locator('.history-row').first().click();
  await modal.getByRole('button', { name: 'Before / after', exact: true }).click();
  await page.setViewportSize({ width: 320, height: 568 });
  await expect(modal.locator('.history-json')).toBeVisible();
  expect(await modal.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  const jsonColumns = modal.locator('.history-json > div');
  const beforeBox = await jsonColumns.nth(0).boundingBox();
  const afterBox = await jsonColumns.nth(1).boundingBox();
  expect(afterBox.y).toBeGreaterThanOrEqual(beforeBox.y + beforeBox.height);
  await snapshotHistory(page, 'History small mobile before and after', testInfo);
  await modal.getByRole('button', { name: 'Close project history' }).click();
  await page.setViewportSize({ width: 1280, height: 720 });

  await page.getByRole('link', { name: 'Home', exact: true }).click();
  await page.getByPlaceholder('New project name').fill(otherProjectName);
  await page.getByRole('button', { name: 'Create project' }).click();
  await page.locator('a.list-group-item').filter({ hasText: otherProjectName }).click();
  await history.click();
  await expect(modal.locator('.history-row')).toHaveCount(1);
  await expect(modal.locator('.history-row')).toContainText(`created project ${otherProjectName}`);
  await modal.getByRole('button', { name: 'Close project history' }).click();
  await page.goto(firstProjectUrl);
  await history.click();
  await expect(modal.locator('.history-row')).toHaveCount(5);
  expect(errors).toEqual([]);
});

test('local date filters use calendar boundaries across daylight saving changes', async ({ page }) => {
  await page.goto('/Identity/Account/Login');
  const bounds = await page.evaluate(async () => {
    const { dateBounds } = await import('/Components/Shared/ProjectHistory.razor.js');
    return [dateBounds('2026-03-08', '2026-03-08'), dateBounds('2026-11-01', '2026-11-01'), dateBounds('', '')];
  });
  expect(bounds[0]).toEqual({ fromUtc: '2026-03-08T08:00:00.000Z', untilUtc: '2026-03-09T07:00:00.000Z' });
  expect(bounds[1]).toEqual({ fromUtc: '2026-11-01T07:00:00.000Z', untilUtc: '2026-11-02T08:00:00.000Z' });
  expect(bounds[2]).toEqual({ fromUtc: null, untilUtc: null });
});
