import { chromium } from "playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.resolve(__dirname, "..");

const scenes = [
  { html: "01-config-state.html", png: "01-config-state.png" },
  { html: "02-chat-typing.html", png: "02-chat-typing.png" },
  { html: "03-keybinds-before.html", png: "03-keybinds-before.png" },
  { html: "04-keybinds-after.html", png: "04-keybinds-after.png" },
  { html: "05-chat-commands.html", png: "05-chat-commands.png" },
];

await mkdir(outDir, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setViewportSize({ width: 1280, height: 720 });

for (const { html, png } of scenes) {
  const fileUrl = `file://${path.join(__dirname, html)}`;
  await page.goto(fileUrl, { waitUntil: "load" });
  await page.waitForTimeout(100);
  await page.screenshot({
    path: path.join(outDir, png),
    type: "png",
  });
  console.log("Wrote", png);
}

await browser.close();
