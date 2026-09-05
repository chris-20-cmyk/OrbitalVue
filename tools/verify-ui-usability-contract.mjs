import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const appPath = join(root, "src/OrbitalVue.Player/App.xaml.cs");
const servicePath = join(root, "src/OrbitalVue.Player/Controls/UiUsabilityService.cs");
const mainWindowPath = join(root, "src/OrbitalVue.Player/MainWindow.xaml");

const [app, service, mainWindow] = await Promise.all([
  readFile(appPath, "utf8"),
  readFile(servicePath, "utf8"),
  readFile(mainWindowPath, "utf8")
]);

const requireText = (source, text, label) => {
  if (!source.includes(text)) {
    console.error(`UI usability verification failed: missing ${label}: ${text}`);
    process.exit(1);
  }
};

requireText(app, "UiUsabilityService.Enable();", "startup registration");
requireText(service, "MinimumReadableFontSize = 12d", "12px readability floor");
requireText(service, "typeof(Control)", "Control readability class handler");
requireText(service, "typeof(TextBlock)", "TextBlock readability class handler");
requireText(service, "PasswordRevealAdorner", "password reveal adorner");
requireText(service, "PasswordChanged +=", "live revealed-password refresh");
requireText(service, "ToolTip = \"Show password\"", "show-password affordance");
requireText(service, "AutomationProperties.SetName", "accessible reveal control name");
requireText(service, "PasswordRevealButtonSpace = 46d", "reserved reveal-button input space");

for (const field of ["XtreamPasswordBox", "PlexTokenBox", "EmbyPasswordBox"]) {
  const pattern = new RegExp(`<PasswordBox[^>]*x:Name=\\"${field}\\"`, "s");
  if (!pattern.test(mainWindow)) {
    console.error(`UI usability verification failed: ${field} is no longer a PasswordBox.`);
    process.exit(1);
  }
}

const tinyFontValues = [...mainWindow.matchAll(/FontSize=\"([0-9]+(?:\.[0-9]+)?)\"/g)]
  .map(match => Number(match[1]))
  .filter(value => value < 12);
if (tinyFontValues.length === 0) {
  console.error("UI usability verification failed: expected legacy explicit micro-fonts for the runtime floor to normalize.");
  process.exit(1);
}

console.log(`UI usability contract: PASS (${tinyFontValues.length} legacy micro-font declarations normalized at runtime)`);
