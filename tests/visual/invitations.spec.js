import { expect, test } from '@chromatic-com/playwright';
import { randomUUID } from 'node:crypto';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';

test.use({ disableAutoSnapshot: true });
test.skip(!process.env.INVITATION_MAIL_DIR, 'Set INVITATION_MAIL_DIR to the app’s development email preview folder.');

async function register(page, email) {
  await page.goto('/Identity/Account/Register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill('InvitationTest1!');
  await page.getByLabel('Confirm password').fill('InvitationTest1!');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page).toHaveURL(/\/projects$/);
}

async function emailLink(recipient) {
  let link;
  await expect.poll(async () => {
    for (const file of await readdir(process.env.INVITATION_MAIL_DIR)) {
      const message = (await readFile(join(process.env.INVITATION_MAIL_DIR, file), 'utf8'))
        .replace(/=\r?\n/g, '').replace(/=3D/g, '=');
      if (!message.includes(recipient)) continue;
      link = message.match(/https?:\/\/[^\s<>"']+\/invitations\/accept\?token=[A-F0-9]{64}/)?.[0];
      if (link) return true;
    }
    return false;
  }).toBe(true);
  return link;
}

async function invite(page, recipient) {
  const id = randomUUID();
  await register(page, `owner-${id}@example.com`);
  const project = `Invitation project ${id}`;
  await page.getByPlaceholder('New project name').fill(project);
  await page.getByRole('button', { name: 'Create project', exact: true }).click();
  await page.locator('a.list-group-item').filter({ hasText: project }).click();
  await page.getByRole('link', { name: 'Settings', exact: true }).click();
  await page.getByLabel('User Email').fill(recipient);
  await page.getByLabel('Role', { exact: true }).selectOption('Editor');
  await page.getByRole('button', { name: 'Send invitation', exact: true }).click();
  await expect(page.locator('.alert-success')).toContainText('Invitation email');
  await expect(page.getByRole('table', { name: 'Pending invitations' }).getByRole('row').filter({ hasText: recipient })).toContainText('Awaiting acceptance');
  return await emailLink(recipient);
}

for (const registered of [false, true]) {
  test(`invited ${registered ? 'existing' : 'new'} user authenticates then explicitly joins`, async ({ page, browser }) => {
    const recipient = `recipient-${randomUUID()}@example.com`;
    if (registered) {
      const setup = await browser.newContext();
      await register(await setup.newPage(), recipient);
      await setup.close();
    }
    const link = await invite(page, recipient);
    const recipientContext = await browser.newContext();
    const recipientPage = await recipientContext.newPage();
    await recipientPage.goto(link);
    const response = await recipientPage.request.get(link);
    expect(response.headers()['referrer-policy']).toBe('no-referrer');
    expect(response.headers()['cache-control']).toContain('no-store');
    await recipientPage.getByRole('link', { name: registered ? 'Log in' : 'Create account', exact: true }).click();
    if (registered) await recipientPage.getByLabel('Email').fill(recipient);
    else await expect(recipientPage.getByLabel('Email')).toHaveValue(recipient);
    await recipientPage.getByLabel('Password', { exact: true }).fill('InvitationTest1!');
    if (!registered) await recipientPage.getByLabel('Confirm password').fill('InvitationTest1!');
    await recipientPage.getByRole('button', { name: registered ? 'Log in' : 'Create account', exact: true }).click();
    await expect(recipientPage).toHaveURL(/\/invitations\/accept\?token=/);
    await expect(recipientPage.getByRole('button', { name: 'Join project', exact: true })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('table', { name: 'Pending invitations' })).toContainText(recipient);
    await recipientPage.getByRole('button', { name: 'Join project', exact: true }).click();
    await expect(recipientPage).toHaveURL(/\/projects\/[^/]+\/home$/);
    await expect(recipientPage.getByRole('heading', { name: 'Feature Flags', exact: true })).toBeVisible();
    await page.reload();
    await expect(page.getByText('No pending invitations.')).toBeVisible();
    await expect(page.locator('table').filter({ has: page.locator('th', { hasText: 'Display Name' }) })).toContainText(recipient);
    await recipientContext.close();
  });
}

test('wrong account cannot accept and revocation invalidates the emailed link', async ({ page, browser }) => {
  const recipient = `recipient-${randomUUID()}@example.com`;
  const link = await invite(page, recipient);
  const otherContext = await browser.newContext();
  const other = await otherContext.newPage();
  await register(other, `wrong-${randomUUID()}@example.com`);
  await other.goto(link);
  await expect(other.getByRole('button', { name: 'Switch account', exact: true })).toBeVisible();
  await expect(other.getByRole('button', { name: 'Join project', exact: true })).toHaveCount(0);
  await other.getByRole('button', { name: 'Switch account', exact: true }).click();
  await expect(other.getByRole('link', { name: 'Create account', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Revoke', exact: true }).click();
  await expect(page.getByText('Invitation revoked.', { exact: true })).toBeVisible();
  await other.reload();
  await expect(other.getByRole('alert')).toContainText('invalid, expired, or no longer available');
  await otherContext.close();
});
