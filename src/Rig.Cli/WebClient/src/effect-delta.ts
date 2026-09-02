export type EffectState = {
  family: string;
  nearestDepth: number;
  viaDispatchOnly: boolean;
  looped: boolean;
};

export type MethodState = {
  id: string;
  name: string;
  signature: string;
  line: number;
  endLine: number;
  effects: EffectState[];
};

export type EffectChangeKind = "same" | "added" | "removed" | "changed";

export type EffectChange = {
  kind: EffectChangeKind;
  base?: EffectState;
  head?: EffectState;
};

export type MethodComparison = {
  base?: MethodState;
  head?: MethodState;
  effects: ReadonlyMap<string, EffectChange>;
};

export type MethodDeltaIndex = {
  baseById: ReadonlyMap<string, MethodComparison>;
  headById: ReadonlyMap<string, MethodComparison>;
  baseByLine: ReadonlyMap<number, MethodComparison[]>;
  headByLine: ReadonlyMap<number, MethodComparison[]>;
};

const emptyIndex = (): MethodDeltaIndex => ({
  baseById: new Map(),
  headById: new Map(),
  baseByLine: new Map(),
  headByLine: new Map(),
});

function sameEffect(base: EffectState, head: EffectState): boolean {
  return base.nearestDepth === head.nearestDepth
    && base.viaDispatchOnly === head.viaDispatchOnly
    && base.looped === head.looped;
}

export function effectChangeAtSite(
  change: EffectChange | undefined,
  side: "old" | "new",
  site: EffectState,
): EffectChangeKind {
  if (!change || change.kind === "same") return "same";
  if (change.kind === "added") return side === "new" ? "added" : "same";
  if (change.kind === "removed") return side === "old" ? "removed" : "same";
  const aggregate = side === "old" ? change.base : change.head;
  return aggregate && sameEffect(aggregate, site) ? "changed" : "same";
}

function compareEffects(base: EffectState[], head: EffectState[]): ReadonlyMap<string, EffectChange> {
  const baseByFamily = new Map(base.map((effect) => [effect.family, effect]));
  const headByFamily = new Map(head.map((effect) => [effect.family, effect]));
  const families = [...new Set([...baseByFamily.keys(), ...headByFamily.keys()])].sort();
  return new Map(families.map((family) => {
    const before = baseByFamily.get(family);
    const after = headByFamily.get(family);
    const kind: EffectChangeKind = !before
      ? "added"
      : !after
        ? "removed"
        : sameEffect(before, after)
          ? "same"
          : "changed";
    return [family, { kind, base: before, head: after }];
  }));
}

function renameShape(method: MethodState): string | null {
  if (!method.id || method.name === "" || method.name.startsWith(".")) return null;
  const open = method.id.indexOf("(");
  const declaration = open < 0 ? method.id : method.id.slice(0, open);
  const member = declaration.lastIndexOf(".");
  if (member < 0) return null;
  const owner = declaration.slice(0, member);
  const parameters = open < 0 ? "" : method.id.slice(open);
  const signature = method.signature
    ? method.signature.replace(method.name, "<method>")
    : "";
  return `${owner}|${signature}|${parameters}`;
}

function addByLine(target: Map<number, MethodComparison[]>, line: number, comparison: MethodComparison): void {
  const rows = target.get(line) || [];
  rows.push(comparison);
  target.set(line, rows);
}

// Compare only methods that can be paired across revisions. Exact symbol identity wins. A renamed method may
// fall back to its owner + normalized signature + parameter shape, but only when that shape is unique on both
// sides; ambiguity fails closed rather than painting unrelated methods as a semantic change. An added/deleted
// file has no pairs and therefore never turns every effect lane into "new"/"gone" noise.
export function buildMethodDeltaIndex(
  baseMethods: MethodState[],
  headMethods: MethodState[],
  comparable: boolean,
): MethodDeltaIndex {
  if (!comparable) return emptyIndex();

  const remainingBase = new Map(baseMethods.map((method) => [method.id, method]));
  const remainingHead = new Map(headMethods.map((method) => [method.id, method]));
  const pairs: Array<[MethodState | undefined, MethodState | undefined]> = [];

  for (const method of baseMethods) {
    const peer = remainingHead.get(method.id);
    if (!peer) continue;
    pairs.push([method, peer]);
    remainingBase.delete(method.id);
    remainingHead.delete(peer.id);
  }

  const shapeGroups = (methods: Iterable<MethodState>): Map<string, MethodState[]> => {
    const groups = new Map<string, MethodState[]>();
    for (const method of methods) {
      const shape = renameShape(method);
      if (!shape) continue;
      const rows = groups.get(shape) || [];
      rows.push(method);
      groups.set(shape, rows);
    }
    return groups;
  };
  const baseShapes = shapeGroups(remainingBase.values());
  const headShapes = shapeGroups(remainingHead.values());
  for (const [shape, before] of baseShapes) {
    const after = headShapes.get(shape);
    if (before.length !== 1 || after?.length !== 1) continue;
    pairs.push([before[0], after[0]]);
    remainingBase.delete(before[0].id);
    remainingHead.delete(after[0].id);
  }

  // A method added/removed inside an otherwise comparable file is a real semantic delta. Shapes that remain
  // on BOTH sides are an ambiguous rename group, so suppress them rather than manufacturing delete+add noise.
  const unresolvedBaseShapes = shapeGroups(remainingBase.values());
  const unresolvedHeadShapes = shapeGroups(remainingHead.values());
  for (const method of remainingBase.values()) {
    const shape = renameShape(method);
    if (shape && unresolvedHeadShapes.has(shape)) continue;
    pairs.push([method, undefined]);
  }
  for (const method of remainingHead.values()) {
    const shape = renameShape(method);
    if (shape && unresolvedBaseShapes.has(shape)) continue;
    pairs.push([undefined, method]);
  }

  const baseById = new Map<string, MethodComparison>();
  const headById = new Map<string, MethodComparison>();
  const baseByLine = new Map<number, MethodComparison[]>();
  const headByLine = new Map<number, MethodComparison[]>();
  for (const [base, head] of pairs) {
    const comparison = { base, head, effects: compareEffects(base?.effects || [], head?.effects || []) };
    if (base) {
      baseById.set(base.id, comparison);
      addByLine(baseByLine, base.line, comparison);
    }
    if (head) {
      headById.set(head.id, comparison);
      addByLine(headByLine, head.line, comparison);
    }
  }

  return { baseById, headById, baseByLine, headByLine };
}

export function changedEffects(comparison: MethodComparison): EffectChange[] {
  return [...comparison.effects.values()].filter((change) => change.kind !== "same");
}
