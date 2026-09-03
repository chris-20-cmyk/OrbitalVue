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

// Windows carries the OrbitalVue name all the way down: binary, data directory and update
// identity. This is a deliberate break -- a Chris.StreamVue install will NOT update across to
// Chris.OrbitalVue, so the first OrbitalVue build is a fresh install, not an upgrade.
requireText(windowsProject, "<AssemblyName>OrbitalVue</AssemblyName>", "Windows assembly");
requireText(windowsProject, "<AssemblyTitle>OrbitalVue</AssemblyTitle>", "Windows file description");
requireText(windowsApp, 'LocalApplicationData), "OrbitalVue"', "Windows data directory");
requireText(windowsManifest, 'Executable="OrbitalVue.exe"', "MSIX executable");
requireText(previewWorkflow, "--packId Chris.OrbitalVue", "Velopack update identity");
requireText(previewWorkflow, "--mainExe OrbitalVue.exe", "Windows update executable");
requireText(previewWorkflow, "--packTitle OrbitalVue", "Windows installer title");

// Two old-brand strings survive on purpose, and both are read-only migration paths. They must
// keep naming the PREVIOUS brand -- a search-and-replace would quietly delete the ability to
// import anything a user exported before the rename.
requireText(maintenance, 'BackupProduct = "OrbitalVue"', "current backup product");
requireText(maintenance, 'LegacyBackupProduct = "StreamVue"', "legacy backup reader");
requireText(maintenance, 'BackupEntropy = Encoding.UTF8.GetBytes("OrbitalVue.PortableBackup.v1")', "current backup entropy");
requireText(maintenance, 'LegacyBackupEntropy = Encoding.UTF8.GetBytes("StreamVue.PortableBackup.v1")', "legacy backup entropy");

// Nothing Windows ships may still carry the old binary or data-directory name.
for (const [source, label] of [
  [windowsProject, "Windows project metadata"],
  [windowsApp, "Windows app startup"],
  [windowsManifest, "Windows Store manifest"],
  [previewWorkflow, "Windows publish workflow"]
]) {
  rejectText(source, "StreamVue.exe", label);
  rejectText(source, "Chris.StreamVue", label);
  rejectText(source, "<AssemblyName>StreamVue", label);
}

console.log("OrbitalVue public identity, Windows binary/update identity and backup migration paths: PASS");
