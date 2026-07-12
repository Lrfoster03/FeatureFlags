import { expect, test } from '@chromatic-com/playwright';

function waitForBlazor(page) {
  return new Promise(resolve => {
    page.on('websocket', socket => {
      if (!socket.url().includes('/_blazor')) return;

      let framesReceived = 0;
      socket.on('framereceived', () => {
        framesReceived += 1;
        if (framesReceived === 2) resolve();
      });
    });
  });
}

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
  const email = `chromatic-${Date.now()}@example.com`;

  await page.goto('/Identity/Account/Register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill('Chromatic1!');
  await page.getByLabel('Confirm password').fill('Chromatic1!');
  const blazorReady = waitForBlazor(page);
  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page).toHaveURL(/\/projects$/);
  await blazorReady;
  await page.getByPlaceholder('New project name').fill('Chromatic Project');
  await page.getByRole('button', { name: 'Create project' }).click();
  await expect(page.getByText("Created project 'Chromatic Project'")).toBeVisible();
  await page.getByRole('link', { name: /Chromatic Project/ }).click();

  await expect(page.getByRole('heading', { name: 'Feature Flags' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Configs' })).toBeVisible();
  await page.locator('.user-email').evaluate(element => {
    element.textContent = 'chromatic@example.com';
  });
});
