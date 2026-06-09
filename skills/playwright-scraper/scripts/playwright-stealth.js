import { chromium } from 'playwright-extra';
import StealthPlugin from 'puppeteer-extra-plugin-stealth';

// Enable stealth plugin to bypass anti-bot detection
chromium.use(StealthPlugin());

const url = process.argv[2];
if (!url) {
  console.error('Usage: node playwright-stealth.js <url>');
  process.exit(1);
}

(async () => {
  const browser = await chromium.launch({
    // Use system-installed Chrome/Edge instead of bundled Chromium
    channel: 'chrome',
    headless: true,
    args: [
      '--no-sandbox',
      '--disable-setuid-sandbox',
      '--disable-blink-features=AutomationControlled',
    ],
  });

  const context = await browser.newContext({
    userAgent:
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    viewport: { width: 1920, height: 1080 },
  });

  const page = await context.newPage();

  try {
    await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
    const content = await page.content();
    console.log(content);
  } catch (error) {
    console.error(`Scrape error: ${error.message}`);
    process.exit(1);
  } finally {
    await browser.close();
  }
})();
