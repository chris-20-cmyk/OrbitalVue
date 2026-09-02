import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const read = (path) => readFile(join(root, path), "utf8");
const fail = (message) => {
  throw new Error(`OrbitalVue brand verification failed: ${message}`);
};
const requireText = (source, fragment, label) => {
  if (!source.includes(fragment)) fail(`${label} is missing ${fragment}`);
};
const rejectText = (source, fragment, label) => {
  if (source.includes(fragment)) fail(`${label} still contains ${fragment}`);
};

const [
  windowsProject,
  windowsApp,
  maintenance,
  windowsManifest,
  previewWorkflow,
  androidProject,
  androidStrings,
  appleProject,
  samsungManifest,
  lgManifest,
  listing,
  privacy,
  releaseContract,
  index,
  privacyPage,
  supportPage
] = await Promise.all([
  read("src/OrbitalVue.Player/OrbitalVue.Player.csproj"),
  read("src/OrbitalVue.Player/App.xaml.cs"),
  read("src/OrbitalVue.Player/Services/OrbitalVueMaintenanceService.cs"),
  read("packaging/windows-msix/AppxManifest.template.xml"),
  read(".github/workflows/publish-preview-assets.yml"),
  read("platforms/android/app/build.gradle.kts"),
  read("platforms/android/app/src/main/res/values/strings.xml"),
  read("platforms/apple/project.yml"),
  read("platforms/tv-web/platform/samsung/config.xml"),
  read("platforms/tv-web/platform/webos/appinfo.json"),
  read("store/store-listing.json"),
  read("store/privacy-data-inventory.json"),
  read("store/cross-platform-release.json"),
  read("site/index.html"),
  read("site/privacy.html"),
  read("site/support.html")
]);

for (const [source, label] of [
  [windowsProject, "Windows project metadata"],
  [windowsManifest, "Windows Store manifest"],
  [androidStrings, "Android app name"],
  [appleProject, "Apple project"],
  [samsungManifest, "Samsung manifest"],
  [lgManifest, "LG manifest"],
  [listing, "Store listing"],
  [index, "public overview"],
  [privacyPage, "public privacy page"],
  [supportPage, "public support page"]
]) {
  requireText(source, "OrbitalVue", label);
}

requireText(androidProject, 'applicationId = "com.orbitalvue.player"', "Android identity");
requireText(appleProject, "PRODUCT_BUNDLE_IDENTIFIER: com.orbitalvue.player", "Apple identity");
requireText(samsungManifest, 'id="OvTvPlayer.OrbitalVue"', "Samsung identity");
requireText(samsungManifest, 'package="OvTvPlayer"', "Samsung package");
requireText(lgManifest, '"id": "com.orbitalvue.player.tv"', "LG identity");
for (const [source, label] of [
  [listing, "Store listing"],
  [privacy, "privacy inventory"],
  [releaseContract, "cross-platform release contract"]
]) {
  // These reject the PREVIOUS brand, so they must keep naming it. A search-and-replace
  // across the tree will happily rewrite them into the current brand and invert the check.
  rejectText(source, "com.streamvue.player", label);
  rejectText(source, "SvTvPlayer.StreamVue", label);
}

// The first OrbitalVue Windows release must retain these private identifiers so
// installed StreamVue builds update in place and can still read protected data.
requireText(windowsProject, "<AssemblyName>StreamVue</AssemblyName>", "Windows bridge assembly");
requireText(windowsProject, "<AssemblyTitle>OrbitalVue</AssemblyTitle>", "Windows file description");
requireText(windowsApp, 'LocalApplicationData), "StreamVue"', "Windows data directory");
requireText(maintenance, 'LegacyBackupProduct = "StreamVue"', "legacy backup reader");
requireText(maintenance, '"StreamVue.PortableBackup.v1"', "backup encryption compatibility");
requireText(previewWorkflow, "--packId Chris.StreamVue", "Velopack update identity");
requireText(previewWorkflow, "--mainExe StreamVue.exe", "Windows update executable");
requireText(previewWorkflow, "--packTitle OrbitalVue", "Windows installer title");

console.log("OrbitalVue public identity and Windows in-place update compatibility: PASS");
