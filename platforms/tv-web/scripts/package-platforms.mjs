import { cp, mkdir, readFile, readdir, rm, unlink, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const webRoot = join(projectRoot, "dist", "web");
const platformRoot = join(projectRoot, "platform");
const assetsRoot = join(projectRoot, "assets");

await packageSamsung();
await packageWebOs();

console.log("Prepared Samsung Tizen and LG webOS package directories.");

async function packageSamsung() {
  const output = join(projectRoot, "dist", "samsung");
  await copyClean(webRoot, output);
  await cp(join(platformRoot, "samsung", "config.xml"), join(output, "config.xml"));
  await cp(join(assetsRoot, "icon-256.png"), join(output, "icon.png"));
  const indexPath = join(output, "index.html");
  const html = await readFile(indexPath, "utf8");
  const samsungScript = '<script src="$WEBAPIS/webapis/webapis.js"></script>';
  await writeFile(indexPath, html.replace("</head>", `  ${samsungScript}\n  </head>`), "utf8");
  await removeSourceMaps(output);
}

async function packageWebOs() {
  const output = join(projectRoot, "dist", "webos");
  await copyClean(webRoot, output);
  await cp(join(platformRoot, "webos", "appinfo.json"), join(output, "appinfo.json"));
  await cp(join(assetsRoot, "icon-80.png"), join(output, "icon.png"));
  await cp(join(assetsRoot, "icon-130.png"), join(output, "largeIcon.png"));
  await cp(join(assetsRoot, "splash-1920x1080.png"), join(output, "splash.png"));
  await removeSourceMaps(output);
}

async function copyClean(source, destination) {
  await rm(destination, { recursive: true, force: true });
  await mkdir(destination, { recursive: true });
  await cp(source, destination, { recursive: true });
}

async function removeSourceMaps(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) await removeSourceMaps(path);
    else if (entry.name.endsWith(".map")) await unlink(path);
  }
}
