import { readFile, readdir } from "node:fs/promises";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { inflateSync } from "node:zlib";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const catalogRoot = join(
  repositoryRoot,
  "platforms",
  "apple",
  "Apps",
  "tvOS",
  "Assets.xcassets"
);
const brandRoot = join(catalogRoot, "App Icon.brandassets");
const expectedAuthor = "com.streamvue.player";

const fail = (message) => {
  throw new Error(`Apple brand asset verification failed: ${message}`);
};
const portable = (value) => value.replaceAll("\\", "/");
const readJSON = async (path) => JSON.parse(await readFile(path, "utf8"));
const walk = async (directory) => {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    if (entry.isDirectory()) {
      files.push(...await walk(join(directory, entry.name)));
    } else if (entry.isFile()) {
      files.push({ name: entry.name, parentPath: directory });
    }
  }
  return files;
};
const validateInfo = (value, label) => {
  if (
    !value ||
    typeof value !== "object" ||
    value.author !== expectedAuthor ||
    value.version !== 1
  ) {
    fail(`${label} must use ${expectedAuthor} and asset-catalog version 1`);
  }
};
const inspectRGBAAlpha = (bytes, width, height, label) => {
  const compressed = [];
  for (let offset = 8; offset + 12 <= bytes.length;) {
    const length = bytes.readUInt32BE(offset);
    const type = bytes.subarray(offset + 4, offset + 8).toString("ascii");
    const end = offset + 12 + length;
    if (end > bytes.length) fail(`${label} contains a truncated PNG chunk`);
    if (type === "IDAT") compressed.push(bytes.subarray(offset + 8, offset + 8 + length));
    offset = end;
    if (type === "IEND") break;
  }
  if (compressed.length === 0) fail(`${label} has no PNG image data`);
  const raw = inflateSync(Buffer.concat(compressed));
  const bytesPerPixel = 4;
  const stride = width * bytesPerPixel;
  if (raw.length !== (stride + 1) * height) fail(`${label} has an unexpected decoded size`);
  let previous = Buffer.alloc(stride);
  let cursor = 0;
  let minimum = 255;
  let maximum = 0;
  const paeth = (left, above, upperLeft) => {
    const estimate = left + above - upperLeft;
    const leftDistance = Math.abs(estimate - left);
    const aboveDistance = Math.abs(estimate - above);
    const upperLeftDistance = Math.abs(estimate - upperLeft);
    return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
      ? left
      : aboveDistance <= upperLeftDistance ? above : upperLeft;
  };
  for (let y = 0; y < height; y += 1) {
    const filter = raw[cursor++];
    if (filter > 4) fail(`${label} uses an invalid PNG row filter`);
    const row = Buffer.alloc(stride);
    for (let x = 0; x < stride; x += 1) {
      const encoded = raw[cursor++];
      const left = x >= bytesPerPixel ? row[x - bytesPerPixel] : 0;
      const above = previous[x];
      const upperLeft = x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;
      const predictor = filter === 1
        ? left
        : filter === 2
          ? above
          : filter === 3
            ? Math.floor((left + above) / 2)
            : filter === 4
              ? paeth(left, above, upperLeft)
              : 0;
      row[x] = (encoded + predictor) & 0xff;
    }
    for (let alpha = 3; alpha < stride; alpha += bytesPerPixel) {
      minimum = Math.min(minimum, row[alpha]);
      maximum = Math.max(maximum, row[alpha]);
    }
    previous = row;
  }
  if (minimum !== 0 || maximum !== 255) {
    fail(`${label} must contain both fully transparent and fully opaque pixels`);
  }
};

const brand = await readJSON(join(brandRoot, "Contents.json"));
validateInfo(brand.info, "brand catalog info");
const expectedRoles = [
  ["App Icon - Large.imagestack", "primary-app-icon", "1280x768"],
  ["App Icon - Small.imagestack", "primary-app-icon", "400x240"],
  ["Top Shelf Image.imageset", "top-shelf-image", "1920x720"],
  ["Top Shelf Image Wide.imageset", "top-shelf-image-wide", "2320x720"]
];
const actualRoles = brand.assets?.map(({ filename, role, size, idiom }) => [
  filename,
  role,
  size,
  idiom
]);
const wantedRoles = expectedRoles.map((entry) => [...entry, "tv"]);
if (JSON.stringify(actualRoles) !== JSON.stringify(wantedRoles)) {
  fail("the tvOS brand catalog roles, sizes, or ordering changed");
}

for (const stackName of ["App Icon - Small.imagestack", "App Icon - Large.imagestack"]) {
  const stack = await readJSON(join(brandRoot, stackName, "Contents.json"));
  validateInfo(stack.info, `${stackName} info`);
  const layers = stack.layers?.map(({ filename }) => filename);
  if (JSON.stringify(layers) !== JSON.stringify([
    "Front.imagestacklayer",
    "Middle.imagestacklayer",
    "Back.imagestacklayer"
  ])) {
    fail(`${stackName} must remain a front, middle, and back parallax stack`);
  }
}

const expectedPNGs = new Map([
  ["App Icon - Large.imagestack/Back.imagestacklayer/Content.imageset/Back.png", [1280, 768, false]],
  ["App Icon - Large.imagestack/Front.imagestacklayer/Content.imageset/Front.png", [1280, 768, true]],
  ["App Icon - Large.imagestack/Middle.imagestacklayer/Content.imageset/Middle.png", [1280, 768, true]],
  ["App Icon - Small.imagestack/Back.imagestacklayer/Content.imageset/Back.png", [400, 240, false]],
  ["App Icon - Small.imagestack/Back.imagestacklayer/Content.imageset/Back@2x.png", [800, 480, false]],
  ["App Icon - Small.imagestack/Front.imagestacklayer/Content.imageset/Front.png", [400, 240, true]],
  ["App Icon - Small.imagestack/Front.imagestacklayer/Content.imageset/Front@2x.png", [800, 480, true]],
  ["App Icon - Small.imagestack/Middle.imagestacklayer/Content.imageset/Middle.png", [400, 240, true]],
  ["App Icon - Small.imagestack/Middle.imagestacklayer/Content.imageset/Middle@2x.png", [800, 480, true]],
  ["Top Shelf Image.imageset/TopShelf.png", [1920, 720, false]],
  ["Top Shelf Image.imageset/TopShelf@2x.png", [3840, 1440, false]],
  ["Top Shelf Image Wide.imageset/TopShelfWide.png", [2320, 720, false]],
  ["Top Shelf Image Wide.imageset/TopShelfWide@2x.png", [4640, 1440, false]]
]);

const files = await walk(brandRoot);
const pngPaths = files
  .filter((entry) => entry.name.toLowerCase().endsWith(".png"))
  .map((entry) => portable(relative(brandRoot, join(entry.parentPath, entry.name))))
  .sort();
const expectedPaths = [...expectedPNGs.keys()].sort();
if (JSON.stringify(pngPaths) !== JSON.stringify(expectedPaths)) {
  fail("the catalog PNG inventory does not exactly match the reviewed tvOS asset set");
}

for (const [assetPath, [expectedWidth, expectedHeight, requiresAlpha]] of expectedPNGs) {
  const bytes = await readFile(join(brandRoot, assetPath));
  if (bytes.length < 33 || bytes.subarray(0, 8).toString("hex") !== "89504e470d0a1a0a") {
    fail(`${assetPath} is not a valid PNG`);
  }
  if (bytes.subarray(12, 16).toString("ascii") !== "IHDR") {
    fail(`${assetPath} has no leading PNG IHDR chunk`);
  }
  const width = bytes.readUInt32BE(16);
  const height = bytes.readUInt32BE(20);
  const bitDepth = bytes[24];
  const colorType = bytes[25];
  if (width !== expectedWidth || height !== expectedHeight) {
    fail(`${assetPath} is ${width}x${height}; expected ${expectedWidth}x${expectedHeight}`);
  }
  const expectedColorType = requiresAlpha ? 6 : 2;
  if (colorType !== expectedColorType) {
    fail(
      requiresAlpha
        ? `${assetPath} must carry a real alpha channel for tvOS parallax`
        : `${assetPath} must be an opaque background or Top Shelf image`
    );
  }
  if (bitDepth !== 8 || bytes[26] !== 0 || bytes[27] !== 0 || bytes[28] !== 0) {
    fail(`${assetPath} must be a non-interlaced 8-bit PNG using standard compression and filtering`);
  }
  if (requiresAlpha) inspectRGBAAlpha(bytes, width, height, assetPath);
}

for (const entry of files.filter((item) => item.name === "Contents.json")) {
  const path = join(entry.parentPath, entry.name);
  const contents = await readJSON(path);
  validateInfo(contents.info, portable(relative(brandRoot, path)));
  for (const image of contents.images ?? []) {
    if (image.idiom !== "tv" || !["1x", "2x"].includes(image.scale)) {
      fail(`${portable(relative(brandRoot, path))} contains an invalid tvOS image declaration`);
    }
    await readFile(join(entry.parentPath, image.filename));
  }
}

const project = await readFile(join(repositoryRoot, "platforms", "apple", "project.yml"), "utf8");
if (!project.includes("ASSETCATALOG_COMPILER_APPICON_NAME: App Icon")) {
  fail("the tvOS target does not select the App Icon brand catalog");
}

console.log("Apple TV layered app icon and Top Shelf assets are structurally valid.");
