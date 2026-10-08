// One-off codemod: reorder Vue SFC top-level blocks to `template -> script -> style`.
// Uses @vue/compiler-sfc for precise block offsets. Preserves CRLF/LF and exact block text.
// Any file with non-whitespace loose text around/between blocks is flagged and skipped.
import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";

const require = createRequire(import.meta.url);
const { parse } = require("vue/compiler-sfc");

const ROOT = path.resolve(process.argv[2] ?? "src");
const APPLY = process.argv.includes("--write");

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.isFile() && p.endsWith(".vue")) out.push(p);
  }
  return out;
}

// Reconstruct the full raw element text (open tag + content + close tag) for a block.
function blockSpan(src, block) {
  const cs = block.loc.start.offset;
  const ce = block.loc.end.offset;
  const start = src.lastIndexOf("<", cs - 1);
  const end = src.indexOf(">", ce) + 1;
  if (start < 0 || end <= 0) throw new Error("cannot locate block tags");
  return { start, end, raw: src.slice(start, end) };
}

function typeName(b) {
  if (b.type === "template") return "template";
  if (b.type === "script" || b.type === "scriptSetup") return "script";
  if (b.type === "style") return "style";
  return "custom";
}

function processFile(file) {
  const src = fs.readFileSync(file, "utf8");
  const { descriptor, errors } = parse(src);
  if (errors && errors.length) return { file, status: "parse-error", errors };

  const eol = src.includes("\r\n") ? "\r\n" : "\n";

  const scripts = [descriptor.script, descriptor.scriptSetup]
    .filter(Boolean)
    .sort((a, b) => a.loc.start.offset - b.loc.start.offset);
  const styles = (descriptor.styles ?? [])
    .slice()
    .sort((a, b) => a.loc.start.offset - b.loc.start.offset);
  const customs = (descriptor.customBlocks ?? [])
    .slice()
    .sort((a, b) => a.loc.start.offset - b.loc.start.offset);

  const desiredBlocks = [
    ...(descriptor.template ? [descriptor.template] : []),
    ...scripts,
    ...styles,
    ...customs,
  ];

  if (desiredBlocks.length === 0) return { file, status: "empty" };

  const spans = desiredBlocks.map((b) => blockSpan(src, b));

  // Verify blocks are in a sane, non-overlapping layout and detect loose text.
  const allSorted = [...spans].sort((a, b) => a.start - b.start);
  const firstStart = allSorted[0].start;
  const lastEnd = allSorted[allSorted.length - 1].end;
  const leading = src.slice(0, firstStart);
  const trailing = src.slice(lastEnd);
  let interLoose = "";
  for (let i = 1; i < allSorted.length; i++) {
    const gap = src.slice(allSorted[i - 1].end, allSorted[i].start);
    if (gap.trim() !== "") interLoose += gap.trim();
  }
  if (leading.trim() || trailing.trim() || interLoose) {
    return { file, status: "flagged", leading, trailing, interLoose };
  }

  // Current order vs desired order.
  const currentTypes = allSorted.map((s) => {
    const b = desiredBlocks.find((bb) => spans[desiredBlocks.indexOf(bb)] === s);
    return typeName(b);
  });
  const desiredTypes = desiredBlocks.map(typeName);
  const alreadyOrdered =
    currentTypes.length === desiredTypes.length &&
    currentTypes.every((t, i) => t === desiredTypes[i]);
  if (alreadyOrdered) return { file, status: "skip" };

  // Rebuild in desired order using spans aligned to desiredBlocks.
  const out = spans.map((s) => s.raw).join(eol + eol) + eol;
  if (APPLY) fs.writeFileSync(file, out, "utf8");
  return { file, status: APPLY ? "written" : "would-write", from: currentTypes.join(","), to: desiredTypes.join(",") };
}

const files = walk(ROOT).sort();
const results = files.map((f) => {
  try {
    return processFile(f);
  } catch (err) {
    return { file: f, status: "exception", message: err.message };
  }
});

const byStatus = {};
for (const r of results) (byStatus[r.status] ??= []).push(r);

console.log(`Scanned ${files.length} .vue files under ${ROOT}`);
for (const [status, list] of Object.entries(byStatus)) {
  console.log(`\n== ${status}: ${list.length} ==`);
  for (const r of list) {
    const rel = path.relative(process.cwd(), r.file);
    let extra = "";
    if (r.from) extra = ` (${r.from} -> ${r.to})`;
    if (r.message) extra = ` ${r.message}`;
    if (r.errors) extra = ` ${JSON.stringify(r.errors.map((e) => e.message))}`;
    if (status === "flagged") extra = ` leading=${JSON.stringify(r.leading)} trailing=${JSON.stringify(r.trailing)} inter=${JSON.stringify(r.interLoose)}`;
    console.log(`  ${rel}${extra}`);
  }
}
