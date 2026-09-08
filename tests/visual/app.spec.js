import { expect, test } from '@chromatic-com/playwright';

import { randomUUID } from 'node:crypto';

test('redirects unauthenticated users to login', async ({ page }) => {
  await page.goto('/projects');

  await expect(page).toHaveURL(/\/Identity\/Account\/Login/);
  await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible();
});

test('shows the login form', async ({ page }) => {
  await page.goto('/Identity/Account/Login');

  await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible();
});

test('shows the registration form', async ({ page }) => {
  await page.goto('/Identity/Account/Register');

  await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible();
});

test('shows an empty project dashboard', async ({ page }) => {
  const testId = randomUUID();
  const email = `chromatic-${testId}@example.com`;
  const expectedProjectName = 'Chromatic Project';
  const projectName = `${expectedProjectName} ${testId}`;

  await page.goto('/Identity/Account/Register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill('Chromatic1!');
  await page.getByLabel('Confirm password').fill('Chromatic1!');
  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page).toHaveURL(/\/projects$/);
  const projectNameInput = page.getByPlaceholder('New project name');
  const projectLink = page.locator('a.list-group-item').filter({ hasText: projectName });

  await expect(projectNameInput).toBeEditable();
  await projectNameInput.fill(projectName);
  await expect(projectNameInput).toHaveValue(projectName);
  await page.getByRole('button', { name: 'Create project' }).click();
  await expect(projectLink).toBeVisible();
  await projectLink.click();

  await expect(page.getByRole('heading', { name: 'Feature Flags' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Configs' })).toBeVisible();
  await page.locator('.user-email').evaluate(element => {
    element.textContent = 'chromatic@example.com';
  });
  await page.locator('.project-switcher button, .project-switcher .dropdown-item').evaluateAll(
    (elements, name) => elements.forEach(element => {
      element.textContent = name;
    }),
    expectedProjectName,
  );
});
