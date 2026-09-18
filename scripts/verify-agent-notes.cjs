const fs = require('fs');
const path = require('path');

const notesDir = path.resolve(__dirname, '..', '.agents', 'notes');

if (!fs.existsSync(notesDir)) {
  console.error(`[verify-notes] 错误: 未找到 Agent 决策笔记根目录: ${notesDir}`);
  process.exit(1);
}

function scanNotes(dir) {
  let noteFiles = [];
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      noteFiles = noteFiles.concat(scanNotes(fullPath));
    } else if (entry.name.endsWith('.md')) {
      noteFiles.push(fullPath);
    }
  }
  return noteFiles;
}

const allNotes = scanNotes(notesDir);
console.log(`[verify-notes] 正在校验 ${allNotes.length} 篇 Agent 决策笔记...`);

let hasError = false;
for (const notePath of allNotes) {
  const content = fs.readFileSync(notePath, 'utf8');
  if (!content.trim()) {
    console.error(`[verify-notes] 空文件警告: ${path.relative(notesDir, notePath)}`);
    hasError = true;
  } else if (!content.startsWith('#')) {
    console.warn(`[verify-notes] 格式提示 (未以顶级标题开头): ${path.relative(notesDir, notePath)}`);
  }
}

if (hasError) {
  console.error('[verify-notes] 校验失败: 存在异常笔记文件。');
  process.exit(1);
}

console.log(`[verify-notes] ✅ 全量 ${allNotes.length} 篇 Agent 决策笔记目录结构与格式校验通过！`);
process.exit(0);
