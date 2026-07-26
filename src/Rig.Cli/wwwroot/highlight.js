// C# syntax highlighting for the inline source panel — a hand-rolled, zero-dependency tokenizer.
//
// WHAT THIS IS: a READABILITY AID, not a parser and emphatically not a semantic model. It is a lexical
// scanner with a two-token lookahead and a capitalised-identifier heuristic for "types". It does not know
// what any name binds to — `Foo` in `Foo.Bar()` is coloured as a type because it starts with a capital,
// whether it is a class, a namespace, a static property, or a local named `Foo`. That is fine for reading
// code; it must NOT be read as rig knowing something. rig's whole thesis is that syntax-only analysis
// mis-resolves C# (which is why the fact pipeline runs Roslyn at index time and freezes real binding into
// facts) — this file is deliberately on the other side of that line, and its output never feeds analysis.
//
// WHY HAND-ROLLED: `rig serve` is a local, offline, air-gapped-capable server; the whole of wwwroot/ has
// zero external dependencies and stays that way. No CDN, no Prism/highlight.js/Shiki.
//
// PUBLIC SURFACE (one function):
//
//   highlightCSharp(lines) -> Array<Array<{ text, cls }>>
//
//     `lines` is an array of raw source line strings (NO trailing newlines), in file order — exactly the
//     `text` fields of an /api/source response. The result has one entry per input line: an ordered array
//     of token runs whose `text` fields concatenate back to the original line EXACTLY (nothing is dropped,
//     added, or re-escaped), and whose `cls` is a CSS class name or "" for "render as plain text".
//
//     The caller builds DOM nodes from the runs (see SourceBody in components.js). Text never becomes
//     markup here — no HTML is produced, so there is nothing to escape and no injection surface. Source
//     full of `<`, `>` and `&` (generics: `HashMap<Guid, string>`) survives as literal text because it is
//     appended with createTextNode downstream. Keep it that way: do not add an innerHTML-producing variant.
//
// CROSS-LINE STATE: the panel renders a whole declaration RANGE, so lines are tokenized as one stream with
// carry-over state, not independently. A line that opens a block comment or a verbatim/raw string leaves
// the scanner in that state, and the next line resumes inside it — so the body of a `/* … */` or a
// multi-line `@"…"` renders as comment/string rather than being re-lexed as code. The carried states are
// exactly: `code`, `block` (inside /* … */), `verbatim` (inside @"…", "" as the escape), and `raw`
// (inside """…""", carrying the opening fence length).
//
// TOKEN CLASSES EMITTED: sx-keyword, sx-type, sx-string, sx-char, sx-number, sx-comment, sx-doc (`///`),
// sx-attr (identifiers inside a line-leading `[…]` attribute list), sx-pre (a `#…` directive line).
// Punctuation and operators stay unclassified on purpose — colouring them is noise, not information.
//
// DELIBERATE NON-GOALS (a syntactic aid may be wrong; it should be wrong QUIETLY):
//   * interpolation holes inside $"…" are painted as part of the string, not re-tokenized as code;
//   * an attribute list is recognised only when `[` is the first non-whitespace on its line, so indexers
//     (`a[i]`), array types (`int[]`) and collection expressions are never mistaken for attributes; the
//     attribute-bracket depth is per-line and is not carried across lines;
//   * the contextual-keyword set is conservative — words that are common ordinary identifiers (`value`,
//     `from`, `select`, `by`, `on`, `equals`, `file`, `with`, `and`, `or`, `not`) are left alone rather
//     than risk painting a variable as a keyword. Over-colouring reads worse than under-colouring.

// Reserved keywords — unambiguous, always painted. The built-in type aliases (`int`, `string`, …) live
// here rather than under types because that is how every C# editor colours them.
const KEYWORDS = new Set([
  "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
  "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
  "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
  "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
  "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
  "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
  "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
  "ushort", "using", "virtual", "void", "volatile", "while",
  // Contextual keywords — only the ones that are rare as ordinary identifiers (see non-goals above).
  "async", "await", "dynamic", "get", "global", "init", "nameof", "nint", "notnull", "nuint",
  "partial", "record", "required", "scoped", "set", "unmanaged", "var", "when", "where", "yield",
]);

const ID_START = /[A-Za-z_]/;
const ID_PART = /[A-Za-z0-9_]/;
// Integer/real literals: hex, binary, decimal with optional fraction/exponent, optional suffix. Written so
// `1..2` (a range) yields `1` and not `1.` — the fraction arm requires a digit after the dot.
const NUMBER = /^(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][+-]?\d+)?)[uUlLfFdDmM]{0,2}/;

// Index just past the closing quote of a verbatim string body starting at `i`, or -1 if it runs off the
// end of the line (the string continues on the next one). `""` is the escape, so a doubled quote is body.
function scanVerbatim(text, i) {
  while (i < text.length) {
    if (text[i] === '"') {
      if (text[i + 1] === '"') {
        i += 2;
        continue;
      }
      return i + 1;
    }
    i++;
  }
  return -1;
}

// Index just past the closing fence of a raw string body starting at `i`, or -1 if it continues on the
// next line. The terminator is the first run of at least `fence` quotes.
function scanRaw(text, i, fence) {
  while (i < text.length) {
    if (text[i] === '"') {
      let j = i;
      while (text[j] === '"') j++;
      if (j - i >= fence) return j;
      i = j;
    } else i++;
  }
  return -1;
}

// Tokenize ONE line given the state carried in from the previous one; returns the runs plus the state to
// carry forward. State is a plain object: { kind: "code" | "block" | "verbatim" | "raw", fence? }.
function tokenizeLine(text, state) {
  const out = [];
  const n = text.length;
  let plain = ""; // buffer of unclassified text, flushed as one run
  const flush = () => {
    if (plain) {
      out.push({ text: plain, cls: "" });
      plain = "";
    }
  };
  const push = (t, cls) => {
    flush();
    if (t) out.push({ text: t, cls });
  };

  let i = 0;
  // ---- resume a construct opened on an earlier line ------------------------------------------------
  if (state.kind === "block") {
    const end = text.indexOf("*/");
    if (end === -1) {
      push(text, "sx-comment");
      return { tokens: out, state };
    }
    push(text.slice(0, end + 2), "sx-comment");
    i = end + 2;
    state = { kind: "code" };
  } else if (state.kind === "verbatim") {
    const end = scanVerbatim(text, 0);
    if (end === -1) {
      push(text, "sx-string");
      return { tokens: out, state };
    }
    push(text.slice(0, end), "sx-string");
    i = end;
    state = { kind: "code" };
  } else if (state.kind === "raw") {
    const end = scanRaw(text, 0, state.fence);
    if (end === -1) {
      push(text, "sx-string");
      return { tokens: out, state };
    }
    push(text.slice(0, end), "sx-string");
    i = end;
    state = { kind: "code" };
  }

  // Attribute-list depth: opened only by a `[` that is the first non-whitespace on the line, so indexers
  // and array types are never mistaken for attributes. Per-line by design (never carried forward).
  let attrDepth = 0;

  // ---- code ---------------------------------------------------------------------------------------
  while (i < n) {
    const c = text[i];

    // preprocessor directive — a `#` that opens the line
    if (c === "#" && text.slice(0, i).trim() === "") {
      push(text.slice(i), "sx-pre");
      i = n;
      break;
    }

    // comments
    if (c === "/" && text[i + 1] === "/") {
      // `///` is an XML doc comment; `////` is a plain (usually commented-out) line.
      const doc = text[i + 2] === "/" && text[i + 3] !== "/";
      push(text.slice(i), doc ? "sx-doc" : "sx-comment");
      i = n;
      break;
    }
    if (c === "/" && text[i + 1] === "*") {
      const end = text.indexOf("*/", i + 2);
      if (end === -1) {
        push(text.slice(i), "sx-comment");
        state = { kind: "block" };
        i = n;
        break;
      }
      push(text.slice(i, end + 2), "sx-comment");
      i = end + 2;
      continue;
    }

    // string literals, with their `$`/`@` prefix run (covers "", @"", $"", $@"", @$"", """…""", $$"""…""")
    if (c === '"' || c === "$" || c === "@") {
      let p = i;
      let verbatim = false;
      while (text[p] === "$" || text[p] === "@") {
        if (text[p] === "@") verbatim = true;
        p++;
      }
      if (text[p] === '"') {
        let q = p;
        while (text[q] === '"') q++;
        const fence = q - p;
        if (verbatim) {
          const end = scanVerbatim(text, p + 1);
          if (end === -1) {
            push(text.slice(i), "sx-string");
            state = { kind: "verbatim" };
            i = n;
            break;
          }
          push(text.slice(i, end), "sx-string");
          i = end;
          continue;
        }
        if (fence >= 3) {
          const end = scanRaw(text, q, fence);
          if (end === -1) {
            push(text.slice(i), "sx-string");
            state = { kind: "raw", fence };
            i = n;
            break;
          }
          push(text.slice(i, end), "sx-string");
          i = end;
          continue;
        }
        // ordinary string: `\` escapes, and an unterminated one simply ends at the line end
        let j = p + 1;
        while (j < n) {
          if (text[j] === "\\") {
            j += 2;
            continue;
          }
          if (text[j] === '"') {
            j++;
            break;
          }
          j++;
        }
        push(text.slice(i, Math.min(j, n)), "sx-string");
        i = Math.min(j, n);
        continue;
      }
      // a bare `$` (or `@` not starting a verbatim identifier) — fall through to the branches below
    }

    // char literal — only when it actually closes on this line, so a stray apostrophe stays plain
    if (c === "'") {
      let j = i + 1;
      let closed = false;
      while (j < n) {
        if (text[j] === "\\") {
          j += 2;
          continue;
        }
        if (text[j] === "'") {
          j++;
          closed = true;
          break;
        }
        j++;
      }
      if (closed) {
        push(text.slice(i, j), "sx-char");
        i = j;
        continue;
      }
      plain += c;
      i++;
      continue;
    }

    // numeric literal
    if (c >= "0" && c <= "9") {
      const m = NUMBER.exec(text.slice(i));
      if (m) {
        push(m[0], "sx-number");
        i += m[0].length;
        continue;
      }
    }

    // identifier / keyword / (heuristic) type — `@` prefixes a verbatim identifier, never a keyword
    if (ID_START.test(c) || (c === "@" && ID_START.test(text[i + 1] || ""))) {
      let j = i + (c === "@" ? 1 : 0);
      while (j < n && ID_PART.test(text[j])) j++;
      const word = text.slice(i, j);
      const bare = c === "@" ? word.slice(1) : word;
      let cls = "";
      if (c !== "@" && KEYWORDS.has(bare)) cls = "sx-keyword";
      // HEURISTIC: a capitalised identifier is painted as a type. There is no semantic model here, so
      // this also catches namespaces, static members, enum members and PascalCase locals. Accepted.
      else if (bare[0] >= "A" && bare[0] <= "Z") cls = attrDepth > 0 ? "sx-attr" : "sx-type";
      if (cls) push(word, cls);
      else plain += word;
      i = j;
      continue;
    }

    // attribute-list bracket tracking
    if (c === "[") {
      if (attrDepth > 0) attrDepth++;
      else if (text.slice(0, i).trim() === "") attrDepth = 1;
    } else if (c === "]" && attrDepth > 0) attrDepth--;

    plain += c;
    i++;
  }
  flush();
  return { tokens: out, state };
}

// highlightCSharp(lines) -> per-line arrays of { text, cls }. See the header for the contract; the short
// version is: concatenating a line's `text` fields reproduces that line byte for byte.
export function highlightCSharp(lines) {
  let state = { kind: "code" };
  return lines.map((line) => {
    const r = tokenizeLine(line ?? "", state);
    state = r.state;
    return r.tokens;
  });
}
