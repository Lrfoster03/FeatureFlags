import { expect, test } from '@chromatic-com/playwright';
import { randomUUID } from 'node:crypto';

test.use({ disableAutoSnapshot: true, isMobile: true, hasTouch: true });

async function expectNoOverflow(page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

for (const width of [320, 390]) {
  test(`navigation and editing fit a ${width}px phone`, async ({ page }, testInfo) => {
    test.setTimeout(90_000);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.setViewportSize({ width, height: 844 });
    const testId = randomUUID();
    const projectName = `Mobile project ${'LongProjectName'.repeat(4)} ${testId}`;
    const otherProjectName = `Second project ${testId}`;
    await page.goto('/Identity/Account/Register');
    await page.getByLabel('Email').fill(`mobile-${randomUUID()}@example.com`);
    await page.getByLabel('Password', { exact: true }).fill('MobileTest1!');
    await page.getByLabel('Confirm password').fill('MobileTest1!');
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(page).toHaveURL(/\/projects$/);
    await page.getByPlaceholder('New project name').fill(projectName);
    await page.getByRole('button', { name: 'Create project' }).click();
    const project = page.locator('a.list-group-item').filter({ hasText: projectName });
    await expect(project).toBeVisible();
    await page.getByPlaceholder('New project name').fill(otherProjectName);
    await page.getByRole('button', { name: 'Create project' }).click();
    await expect(page.locator('a.list-group-item').filter({ hasText: otherProjectName })).toBeVisible();
    await expectNoOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('projects-mobile.png'), fullPage: true });
    await project.click();

    const navigation = page.getByRole('navigation', { name: 'Main navigation' });
    const toggle = navigation.getByRole('button', { name: 'Toggle navigation' });
    const settings = navigation.getByRole('link', { name: 'Settings', exact: true });
    const history = navigation.getByRole('button', { name: 'Project history', exact: true, includeHidden: true });
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(settings).not.toBeVisible();
    expect((await toggle.boundingBox()).height).toBeGreaterThanOrEqual(44);
    await toggle.focus();
    await page.keyboard.press('Enter');
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await expect(page.locator('#app-navigation')).toHaveClass(/\bshow\b/);
    await expect(settings).toBeVisible();
    await expect(history).toBeVisible();
    await expectNoOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('navigation-mobile.png'), fullPage: true });
    await history.click();
    const dialog = page.locator('dialog[open]');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Close project history' }).click();
    await expect(history).toBeFocused();
    await toggle.click();
    await expect(settings).not.toBeVisible();

    const switcher = navigation.getByRole('button', { name: 'Switch project' });
    await switcher.click();
    await expectNoOverflow(page);
    await navigation.getByRole('link', { name: otherProjectName, exact: true }).click();
    await expect(switcher).toHaveText(otherProjectName);
    await switcher.click();
    await navigation.getByRole('link', { name: projectName, exact: true }).click();
    await expect(switcher).toHaveText(projectName);

    await page.getByRole('button', { name: 'Add Flag', exact: true }).click();
    const flag = page.locator('.feature-pill').first();
    await flag.locator('.pill-header').click();
    await flag.locator('.rollout-input').fill('35');
    await flag.locator('.rollout-input').press('Tab');
    await expect(page.getByText('You have unsaved changes.')).toBeVisible();
    await expectNoOverflow(page);
    for (const name of ['Save', 'Discard']) {
      const button = page.getByRole('button', { name, exact: true });
      await expect(button).toBeInViewport();
      expect((await button.boundingBox()).height).toBeGreaterThanOrEqual(44);
    }
    await page.screenshot({ path: testInfo.outputPath('flag-edit-mobile.png'), fullPage: true });
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('You have unsaved changes.')).toHaveCount(0);
    await expect(flag).toContainText('35%');

    await page.getByRole('button', { name: 'Add Config', exact: true }).click();
    const config = page.locator('.feature-pill').last();
    await expect(page.locator('.feature-pill')).toHaveCount(2);
    await config.locator('.pill-header').click();
    const tools = config.getByRole('button', { name: 'JSON value tools' });
    await expect(config.locator('.value-format-button')).not.toBeVisible();
    await tools.click();
    await expect(config.locator('.value-format-button')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(tools).toBeFocused();
    await expect(config.locator('.value-format-button')).not.toBeVisible();
    await config.locator('.config-value-input').fill('{"enabled":true,"label":"Mobile checkout"}');
    await config.locator('.config-value-input').press('Tab');
    await tools.click();
    await config.locator('.value-format-button').click();
    await expect(config.locator('.config-value-input')).toHaveValue(/\n/);
    await expectNoOverflow(page);
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }));
    await page.screenshot({ path: testInfo.outputPath('config-edit-mobile.png'), fullPage: true });
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('You have unsaved changes.')).toHaveCount(0);

    await toggle.click();
    await settings.click();
    await expect(page.getByRole('heading', { name: 'Project Settings' })).toBeVisible();
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(history).toBeEnabled();
    await page.getByPlaceholder('Local dev, production app, etc.').fill('Mobile SDK');
    await page.getByRole('button', { name: 'Generate Key', exact: true }).click();
    const key = page.getByRole('table', { name: 'API keys' }).locator('tbody tr').filter({ hasText: 'Mobile SDK' });
    await expect(key).toBeVisible();
    await key.getByRole('button', { name: 'Revoke', exact: true }).scrollIntoViewIfNeeded();
    await expectNoOverflow(page);
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }));
    await page.screenshot({ path: testInfo.outputPath('settings-mobile.png'), fullPage: true });

    for (const resizedWidth of [768, 991, 992, 1280, width]) {
      await page.setViewportSize({ width: resizedWidth, height: 844 });
      if (resizedWidth < 992) {
        await expect(toggle).toBeVisible();
        await expect(settings).not.toBeVisible();
      } else {
        await expect(toggle).not.toBeVisible();
        await expect(settings).toBeVisible();
      }
      await expectNoOverflow(page);
    }
    await page.setViewportSize({ width: 667, height: 320 });
    await toggle.click();
    await expect(page.locator('#app-navigation')).toHaveClass(/\bshow\b/);
    const logout = navigation.getByRole('button', { name: 'Logout', exact: true });
    await logout.scrollIntoViewIfNeeded();
    await expect(logout).toBeInViewport();
    await expect(toggle).toBeInViewport();
    await expectNoOverflow(page);
    expect(errors).toEqual([]);
  });
}
