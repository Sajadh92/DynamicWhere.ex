import sharp from "sharp";
import { promises as fs } from "fs";
import path from "path";

const SRC = "D:/Project/DynamicWhere.ex/dw-logo.png";

const TARGETS = [
  // Next.js auto-served favicon (replaces default favicon)
  { out: "D:/Project/DynamicWhere.ex/OfficialWebsite/app/icon.png", size: 512 },
  // iOS home-screen icon
  { out: "D:/Project/DynamicWhere.ex/OfficialWebsite/app/apple-icon.png", size: 180 },
  // Public-served logo (header/footer)
  { out: "D:/Project/DynamicWhere.ex/OfficialWebsite/public/logo.png", size: 512 },
  { out: "D:/Project/DynamicWhere.ex/OfficialWebsite/public/logo-32.png", size: 32 },
  { out: "D:/Project/DynamicWhere.ex/OfficialWebsite/public/logo-128.png", size: 128 },
  // OG image (square crop fits 1200x630 minimums)
  { out: "D:/Project/DynamicWhere.ex/OfficialWebsite/public/og-image.png", size: 1200 },
  // NuGet package icon
  { out: "D:/Project/DynamicWhere.ex/DynamicWhere.ex/icon.png", size: 512 },
];

for (const { out, size } of TARGETS) {
  await fs.mkdir(path.dirname(out), { recursive: true });
  await sharp(SRC)
    .resize(size, size, { fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png({ compressionLevel: 9, quality: 90 })
    .toFile(out);
  const { size: bytes } = await fs.stat(out);
  console.log(`${out}  ${size}x${size}  ${bytes} bytes`);
}

console.log("done");
