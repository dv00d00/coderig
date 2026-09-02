// Session-only disclosure state and a DOM-free projection of the review file tree.
const normalizePath = (path) => path.replaceAll("\\", "/").split("/").filter(Boolean).join("/");
const scope = (s) => JSON.stringify([s.reviewBase, s.reviewHead]);
const mode = (s) => s.reviewFileSearch.trim() ? "search" : "normal";

export function collapsedReviewFolders(s) {
  return new Set(s.reviewFolderCollapse[mode(s)][scope(s)] || []);
}

export function toggleReviewFolder(s, path) {
  const collapsed = collapsedReviewFolders(s);
  const key = normalizePath(path);
  if (collapsed.has(key)) collapsed.delete(key);
  else collapsed.add(key);
  const bucket = mode(s);
  return {
    reviewFolderCollapse: {
      ...s.reviewFolderCollapse,
      [bucket]: { ...s.reviewFolderCollapse[bucket], [scope(s)]: [...collapsed] },
    },
  };
}

export function setReviewFolderSearch(s, value) {
  return {
    reviewFileSearch: value,
    // Search starts expanded so a match cannot hide behind a normal-mode collapse. Its own disclosures
    // remain usable until the next query edit; clearing search restores the untouched normal state.
    reviewFolderCollapse: { ...s.reviewFolderCollapse, search: {} },
  };
}

export function reviewTreeRows(files, collapsed, pathOf = (file) => file.path) {
  const root = { dirs: new Map(), files: [] };
  for (const file of files) {
    const segments = normalizePath(pathOf(file)).split("/");
    segments.pop();
    let node = root;
    for (const segment of segments) {
      if (!node.dirs.has(segment)) node.dirs.set(segment, { dirs: new Map(), files: [] });
      node = node.dirs.get(segment);
    }
    node.files.push(file);
  }

  const rows = [];
  const visit = (node, parent = "", depth = 0) => {
    for (const [name, child] of [...node.dirs.entries()].sort(([a], [b]) => a.localeCompare(b))) {
      const path = parent ? `${parent}/${name}` : name;
      const expanded = !collapsed.has(path);
      rows.push({ kind: "folder", path, name, depth, expanded });
      if (expanded) visit(child, path, depth + 1);
    }
    for (const file of [...node.files].sort((a, b) => pathOf(a).localeCompare(pathOf(b))))
      rows.push({ kind: "file", file, depth });
  };
  visit(root);
  return rows;
}
