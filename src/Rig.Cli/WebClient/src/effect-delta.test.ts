import assert from "node:assert/strict";
import test from "node:test";
import {
  buildMethodDeltaIndex,
  changedEffects,
  effectChangeAtSite,
  type EffectState,
  type MethodState,
} from "./effect-delta.ts";

const effect = (
  family: string,
  nearestDepth: number,
  viaDispatchOnly = false,
  looped = false,
): EffectState => ({ family, nearestDepth, viaDispatchOnly, looped });

const method = (
  id: string,
  name: string,
  line: number,
  effects: EffectState[],
  signature = `void ${name}()`,
): MethodState => ({ id, name, signature, line, endLine: line + 5, effects });

test("method delta compares reached families instead of rewritten lines", () => {
  const id = "M:Demo.Work.Run";
  const index = buildMethodDeltaIndex(
    [method(id, "Run", 10, [effect("db", 1)])],
    [method(id, "Run", 80, [effect("db", 1), effect("cache", 0)])],
    true,
  );

  const comparison = index.headById.get(id)!;
  assert.deepEqual(changedEffects(comparison).map((change) => change.kind), ["added"]);
  assert.equal(comparison.effects.get("db")?.kind, "same");
  assert.equal(index.headByLine.get(80)?.[0], comparison);
});

test("looping and dispatch proof changes are semantic deltas even at the same depth", () => {
  const id = "M:Demo.Work.Run";
  const index = buildMethodDeltaIndex(
    [method(id, "Run", 10, [effect("db", 0, true, false), effect("io", 0, false, false)])],
    [method(id, "Run", 10, [effect("db", 0, false, false), effect("io", 0, false, true)])],
    true,
  );

  const comparison = index.headById.get(id)!;
  assert.equal(comparison.effects.get("db")?.kind, "changed");
  assert.equal(comparison.effects.get("io")?.kind, "changed");
});

test("a changed method aggregate marks only the site that establishes that aggregate", () => {
  const id = "M:Demo.Work.Run";
  const index = buildMethodDeltaIndex(
    [method(id, "Run", 10, [effect("db", 2, true)])],
    [method(id, "Run", 10, [effect("db", 0, false, true)])],
    true,
  );
  const change = index.headById.get(id)?.effects.get("db");

  assert.equal(effectChangeAtSite(change, "new", effect("db", 0, false, true)), "changed");
  assert.equal(effectChangeAtSite(change, "new", effect("db", 4, false, false)), "same");
  assert.equal(effectChangeAtSite(change, "old", effect("db", 2, true)), "changed");
});

test("added files do not paint every method as a new semantic delta", () => {
  const index = buildMethodDeltaIndex(
    [],
    [method("M:Demo.Work.Run", "Run", 10, [effect("db", 0)])],
    false,
  );

  assert.equal(index.headById.size, 0);
  assert.equal(index.headByLine.size, 0);
});

test("a unique rename shape pairs across symbol-id changes", () => {
  const index = buildMethodDeltaIndex(
    [method("M:Demo.Work.Before(System.Int32)", "Before", 10, [effect("db", 1)], "void Before(int id)")],
    [method("M:Demo.Work.After(System.Int32)", "After", 20, [effect("db", 0)], "void After(int id)")],
    true,
  );

  const comparison = index.headById.get("M:Demo.Work.After(System.Int32)")!;
  assert.equal(comparison.base?.name, "Before");
  assert.equal(comparison.effects.get("db")?.kind, "changed");
});

test("an ambiguous rename shape with unequal sides fails closed", () => {
  const index = buildMethodDeltaIndex(
    [
      method("M:Demo.Work.Start", "Start", 10, [effect("db", 0)]),
      method("M:Demo.Work.Stop", "Stop", 20, [effect("io", 0)]),
    ],
    [
      method("M:Demo.Work.Begin", "Begin", 30, [effect("db", 0)]),
      method("M:Demo.Work.End", "End", 40, [effect("io", 0)]),
      method("M:Demo.Work.Reset", "Reset", 50, [effect("io", 0)]),
    ],
    true,
  );

  assert.equal(index.baseById.size, 0);
  assert.equal(index.headById.size, 0);
});

test("a renamed property accessor pairs on its normalized member position", () => {
  const index = buildMethodDeltaIndex(
    [method(
      "M:MedDBase.Search.get_ContractSlotWindow",
      "get_ContractSlotWindow",
      10,
      [effect("db", 4, true)],
      "MedDBase.Search.ContractSlotWindow.get",
    )],
    [method(
      "M:MedDBase.Search.get_ContractActiveRange",
      "get_ContractActiveRange",
      12,
      [effect("db", 4, true)],
      "MedDBase.Search.ContractActiveRange.get",
    )],
    true,
  );

  const comparison = index.headById.get("M:MedDBase.Search.get_ContractActiveRange")!;
  assert.equal(comparison.base?.name, "get_ContractSlotWindow");
  assert.deepEqual(changedEffects(comparison), []);
});

test("a getter never pairs with a setter", () => {
  const index = buildMethodDeltaIndex(
    [method(
      "M:MedDBase.Search.get_ContractSlotWindow",
      "get_ContractSlotWindow",
      10,
      [effect("db", 4, true)],
      "MedDBase.Search.ContractSlotWindow.get",
    )],
    [method(
      "M:MedDBase.Search.set_ContractActiveRange",
      "set_ContractActiveRange",
      12,
      [effect("db", 4, true)],
      "MedDBase.Search.ContractActiveRange.set",
    )],
    true,
  );

  assert.equal(index.baseById.get("M:MedDBase.Search.get_ContractSlotWindow")?.head, undefined);
  assert.equal(index.headById.get("M:MedDBase.Search.set_ContractActiveRange")?.base, undefined);
});

test("two accessors renamed into the same member shape pair by ascending source line", () => {
  const index = buildMethodDeltaIndex(
    [
      method(
        "M:MedDBase.Search.get_ContractSlotWindow",
        "get_ContractSlotWindow",
        10,
        [effect("db", 4, true)],
        "MedDBase.Search.ContractSlotWindow.get",
      ),
      method(
        "M:MedDBase.Search.get_CourseSlotWindow",
        "get_CourseSlotWindow",
        20,
        [effect("cache", 6, true)],
        "MedDBase.Search.CourseSlotWindow.get",
      ),
    ],
    [
      method(
        "M:MedDBase.Search.get_ContractActiveRange",
        "get_ContractActiveRange",
        10,
        [effect("db", 4, true)],
        "MedDBase.Search.ContractActiveRange.get",
      ),
      method(
        "M:MedDBase.Search.get_CourseActiveRange",
        "get_CourseActiveRange",
        20,
        [effect("cache", 6, true)],
        "MedDBase.Search.CourseActiveRange.get",
      ),
    ],
    true,
  );

  const contract = index.headById.get("M:MedDBase.Search.get_ContractActiveRange")!;
  const course = index.headById.get("M:MedDBase.Search.get_CourseActiveRange")!;
  assert.equal(contract.base?.name, "get_ContractSlotWindow");
  assert.equal(course.base?.name, "get_CourseSlotWindow");
  assert.deepEqual(changedEffects(contract), []);
  assert.deepEqual(changedEffects(course), []);
});

test("a line-paired member still reports a real effect difference", () => {
  const index = buildMethodDeltaIndex(
    [
      method(
        "M:MedDBase.Search.get_ContractSlotWindow",
        "get_ContractSlotWindow",
        10,
        [effect("db", 4, true)],
        "MedDBase.Search.ContractSlotWindow.get",
      ),
      method(
        "M:MedDBase.Search.get_CourseSlotWindow",
        "get_CourseSlotWindow",
        20,
        [effect("cache", 6, true)],
        "MedDBase.Search.CourseSlotWindow.get",
      ),
    ],
    [
      method(
        "M:MedDBase.Search.get_ContractActiveRange",
        "get_ContractActiveRange",
        10,
        [effect("db", 4, true)],
        "MedDBase.Search.ContractActiveRange.get",
      ),
      method(
        "M:MedDBase.Search.get_CourseActiveRange",
        "get_CourseActiveRange",
        20,
        [effect("cache", 2, true), effect("db", 0)],
        "MedDBase.Search.CourseActiveRange.get",
      ),
    ],
    true,
  );

  assert.deepEqual(changedEffects(index.headById.get("M:MedDBase.Search.get_ContractActiveRange")!), []);
  const course = index.headById.get("M:MedDBase.Search.get_CourseActiveRange")!;
  assert.equal(course.effects.get("cache")?.kind, "changed");
  assert.equal(course.effects.get("db")?.kind, "added");
});

test("a shape group with a line repeated on one side fails closed", () => {
  const index = buildMethodDeltaIndex(
    [
      method(
        "M:MedDBase.Search.get_ContractSlotWindow",
        "get_ContractSlotWindow",
        10,
        [effect("db", 4, true)],
        "MedDBase.Search.ContractSlotWindow.get",
      ),
      method(
        "M:MedDBase.Search.get_CourseSlotWindow",
        "get_CourseSlotWindow",
        10,
        [effect("cache", 6, true)],
        "MedDBase.Search.CourseSlotWindow.get",
      ),
    ],
    [
      method(
        "M:MedDBase.Search.get_ContractActiveRange",
        "get_ContractActiveRange",
        12,
        [effect("db", 4, true)],
        "MedDBase.Search.ContractActiveRange.get",
      ),
      method(
        "M:MedDBase.Search.get_CourseActiveRange",
        "get_CourseActiveRange",
        20,
        [effect("cache", 6, true)],
        "MedDBase.Search.CourseActiveRange.get",
      ),
    ],
    true,
  );

  assert.equal(index.baseById.size, 0);
  assert.equal(index.headById.size, 0);
});

test("a renamed plain method still pairs on its normalized signature", () => {
  const index = buildMethodDeltaIndex(
    [method(
      "M:MedDBase.Search.ServicesListUpdated",
      "ServicesListUpdated",
      10,
      [effect("db", 1)],
      "MedDBase.Search.ServicesListUpdated()",
    )],
    [method(
      "M:MedDBase.Search.ServicesRefreshed",
      "ServicesRefreshed",
      14,
      [effect("db", 0)],
      "MedDBase.Search.ServicesRefreshed()",
    )],
    true,
  );

  const comparison = index.headById.get("M:MedDBase.Search.ServicesRefreshed")!;
  assert.equal(comparison.base?.name, "ServicesListUpdated");
  assert.equal(comparison.effects.get("db")?.kind, "changed");
});

test("a method added inside a modified file is new, while the file-level added case stays quiet", () => {
  const stable = method("M:Demo.Work.Stable", "Stable", 5, [effect("io", 0)]);
  const added = method("M:Demo.Work.Added(System.Int32)", "Added", 30, [effect("db", 0)]);
  const index = buildMethodDeltaIndex([stable], [stable, added], true);

  assert.equal(index.headById.get(added.id)?.effects.get("db")?.kind, "added");
});
