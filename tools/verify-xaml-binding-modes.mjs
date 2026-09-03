// Some WPF dependency properties are registered with BindsTwoWayByDefault, so `{Binding Foo}`
// silently becomes a TwoWay binding. Point one at a getter-only property and nothing warns --
// WPF throws XamlParseException when the template is applied. Templates are applied during
// list virtualisation, so the app dies while rendering rows rather than at startup, and it
// keeps dying on every launch once the offending items are cached.
//
// That is how `Value="{Binding WatchProgressPercent}"` on the channel-row ProgressBar took
// the Windows app down the moment a playlist produced channels: a TwoWay binding onto
// `public double WatchProgressPercent => ...`, shipped in 5.6, 5.7 and 5.8.0-alpha.1.
//
// This requires an explicit Mode on every binding whose target defaults to TwoWay, and
// rejects a Mode=TwoWay pointed at a property with no setter.

import { readFile, readdir } from "node:fs/promises";
import { dirname, join, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const appDirectory = join(root, "src/OrbitalVue.Player");

// element -> the properties WPF registers with BindsTwoWayByDefault
const twoWayByDefault = new Map([
  ["ProgressBar", ["Value"]],
  ["Slider", ["Value"]],
  ["ScrollBar", ["Value"]],
  ["TextBox", ["Text"]],
  ["ComboBox", ["Text", "SelectedItem", "SelectedIndex", "SelectedValue"]],
  ["ListBox", ["SelectedItem", "SelectedIndex", "SelectedValue"]],
  ["ListView", ["SelectedItem", "SelectedIndex", "SelectedValue"]],
  ["DataGrid", ["SelectedItem", "SelectedIndex", "SelectedValue"]],
  ["TreeView", ["SelectedItem"]],
  ["TabControl", ["SelectedItem", "SelectedIndex"]],
  ["CheckBox", ["IsChecked"]],
  ["RadioButton", ["IsChecked"]],
  ["ToggleButton", ["IsChecked"]],
  ["DatePicker", ["SelectedDate"]]
]);

const filesUnder = async (directory, extension) => {
  const found = [];
  for (const entry of await readdir(directory, { withFileTypes: true, recursive: true })) {
    if (entry.isFile() && entry.name.endsWith(extension)) {
      found.push(join(entry.parentPath ?? entry.path, entry.name));
    }
  }
  return found;
};

// Expression-bodied members (`public double Foo => ...`) compile to a getter with no setter,
// which is what WPF rejects. Auto-properties and positional record parameters are writable.
const gettersOnly = new Set();
const settable = new Set();
for (const file of await filesUnder(appDirectory, ".cs")) {
  const source = await readFile(file, "utf8");
  for (const match of source.matchAll(/public\s+[\w<>?[\],\s]+?\s(\w+)\s*=>/g)) {
    gettersOnly.add(match[1]);
  }
  for (const match of source.matchAll(/public\s+[\w<>?[\],\s]+?\s(\w+)\s*\{[^}]*?\b(?:set|init)\b/g)) {
    settable.add(match[1]);
  }
  for (const match of source.matchAll(/^\s{4,}[\w<>?[\],]+\s+(\w+),\s*$/gm)) {
    settable.add(match[1]);
  }
}

const tagPattern = /<([A-Za-z][\w.]*)((?:[^<>"]|"[^"]*")*)\/?>/g;
const attributePattern = /([\w.:]+)\s*=\s*"(\{Binding[^"]*\})"/g;
const bindingPathPattern = /\{Binding\s+(?:Path\s*=\s*)?([A-Za-z_]\w*)/;
const bindingModePattern = /\bMode\s*=\s*(\w+)/;

const problems = [];
let inspected = 0;

for (const file of await filesUnder(appDirectory, ".xaml")) {
  const source = await readFile(file, "utf8");
  const relative = file.slice(root.length + 1).split(sep).join("/");

  for (const tag of source.matchAll(tagPattern)) {
    const properties = twoWayByDefault.get(tag[1]);
    if (properties === undefined) continue;

    for (const attribute of tag[2].matchAll(attributePattern)) {
      if (!properties.includes(attribute[1])) continue;
      inspected += 1;

      const expression = attribute[2];
      const line = source.slice(0, tag.index).split("\n").length;
      const mode = expression.match(bindingModePattern)?.[1];
      const path = expression.match(bindingPathPattern)?.[1];
      const where = `${relative}:${line}  <${tag[1]} ${attribute[1]}="${expression}"`;

      if (mode === undefined) {
        problems.push(`${where}\n      ${tag[1]}.${attribute[1]} binds TwoWay by default. ` +
          `State Mode explicitly -- if '${path}' has no setter this crashes at render time.`);
      } else if (mode === "TwoWay" && path !== undefined &&
                 gettersOnly.has(path) && !settable.has(path)) {
        problems.push(`${where}\n      Mode=TwoWay targets '${path}', which has no setter.`);
      }
    }
  }
}

if (inspected === 0) {
  console.error("XAML binding-mode verification failed: matched no bindings at all, " +
    "which means this check is not looking at anything. Fix the scan before trusting it.");
  process.exit(1);
}

if (problems.length > 0) {
  console.error(`XAML binding-mode verification failed (${problems.length}):\n`);
  for (const problem of problems) console.error(`  ${problem}\n`);
  process.exit(1);
}

console.log(`All ${inspected} two-way-capable XAML bindings declare an explicit Mode: PASS`);
