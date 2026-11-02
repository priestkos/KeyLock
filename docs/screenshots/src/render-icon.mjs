import { chromium } from "playwright";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const iconSvg = path.resolve(__dirname, "../../../images/icon.svg");
const iconPng = path.resolve(__dirname, "../../../images/icon.png");

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setViewportSize({ width: 512, height: 512 });
await page.goto(`file://${iconSvg}`, { waitUntil: "load" });
await page.screenshot({ path: iconPng, type: "png" });
console.log("Wrote", iconPng);
await browser.close();
