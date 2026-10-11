# Hover for names and expressions

Plan, 10 Oct 2026.

## Status, 10 Oct 2026

All seven phases are built and tested, with these changes from the plan:

- **Shared code:** `Hovers/HoverText.cs` (phase 1), `Hovers/HoverSnapshot.cs` (phase 3) and `Hovers/HoverBuilder.cs`. The builder makes a `HoverResult` model, rendered as Markdown by the language server and as a value with children by the debugger, as VSCode shows a debug hover as an expandable value rather than Markdown.
- **The snapshot** lives on `ServiceManager.HoverSnapshot`, set by `ProjectBuilder` after a whole build succeeds. The debug session copies it at launch (`DebugHover.SetSnapshot`), because in the shared background process the language server's rebuilds use the same `ServiceManager`.
- **Debug hover scope (open question 3):** neither a qualified name nor a custom position. DAP's `evaluate` has `source`, `line` and `column` for hovers; the protocol library here (17.2) doesn't know them, so `DebugHover` reads them from its non-public `AdditionalProperties`. With no position, or a file that's changed, the hover falls back to evaluating the text as before, and shows nothing if that fails.
- **Raw IO state (open question 4):** not needed. The hover never reads `$9f00` to `$9fff`; it says the address is IO instead.
- **Compiler additions:** `CompileState.ProcedureAt` and `CreateEvaluator`, `Line.AccessMode`, and a `Variables.TryGetValueIgnoringVisibility` overload that gives the owner.
- **Not verified in VSCode:** that VSCode sends `source`/`line`/`column` on a hover evaluate. The tests send them as VSCode's JSON would.

Hovering over a name in a `.bmasm` file shows what it is and its value. Hovering over an operand or expression (`#COUNT+1`, `lines,x`) shows its value and how it's worked out. While a debug session is paused, the same hover shows live values: memory, registers and the effective address.

## Where things stand

- **Language server hover:** `LSP/HoverHandler.cs` only knows the 78 KERNAL routines (`X16KernelDocumentation.cs`, via `TokenDescriptionService`). It finds the word by splitting on whitespace (`GetWordAtCharIndex`), so `#VALUE`, `lines,x` and `<buffer` never match.
- **The language server already builds the project** after every edit (`FileChangeHandler.UpdateFileChanges`, debounced, through the shared `ServiceManager`), so the compile state with every name is available. Nothing keeps it for the hover.
- **The compiler knows each line's scope,** but only for lines that produce output: `Line.Procedure` / `Line.Scope`, `Line.Source`. Directives, labels on their own lines, and blank or comment lines aren't recorded.
- **Debugger hover:** X16D doesn't set `SupportsEvaluateForHovers`. `HandleEvaluateRequest` passes every request to `ExpressionManager.Evaluate`, which ignores `context` and looks names up through `state.Evaluator.Variables`, not the line being hovered over.
- **The extension** registers the language client for `bmasm` (`extension.ts`), but no `EvaluatableExpressionProvider`, so VS Code picks the debug hover text using its default word pattern.

## Rules

- **What's under the cursor:**
  - On a name, the hover is about that name, followed by the whole operand if there's more to it.
  - Elsewhere in an operand or a directive's expression, it's about the whole expression.
- **A name's scope** comes from the line being hovered over, not from wherever execution has stopped.
- **The language server shows build-time values:** a constant's value, a label's address, a variable's address and type. It never reads memory.
- **The debugger shows run-time values:** it uses the session's own compile state (never the language server's latest build, which may be newer than what's running), the current registers and memory.
- **A debug hover never changes the machine:** reads come from raw state, never through the emulated CPU's read path. Hovering over `VERA_DATA0` mustn't move VERA's address, and hovering over a VIA register mustn't clear its interrupt flags.
- **Visibility is ignored,** as everywhere else in the debugger. A private name in another scope still shows, and is marked private.

## Phase 1: what's under the cursor (shared)

A helper, used by both the language server and the extension, that takes a line and a column and returns:

- **the name** under the cursor: letters, digits, `_` and `:`, with any leading or trailing `:` trimmed (`source:` gives `source`; `text:line_count` stays whole); and
- **the operand or expression** containing it, split into the addressing-mode parts (`#`, `,x`, `,y`, the indirect brackets) and the value expression.

Take the operand from the compiler's own parse where possible (`Line`'s parts, operand labels already split off) rather than a second parser. For a line with no compiled `Line` (a directive), split on the directive's own parameters.

`$ff`, `%1010`, `'A'` and operators give no name. Anonymous labels (`-`, `+`, `--name`) only count as names inside an instruction's operand, where the compiled line already knows what they mean.

The KERNAL hover uses the same name rule, so `jmp BSOUT,` style cases work too.

## Phase 2: each source line's scope (compiler)

In `Compiler.Parse`, record `state.Procedure` against the `SourceFilePosition` of every line on the pass that processes it, directives included. Keep it on `CompileState` as a lookup from file and line to `Procedure`.

- **Lines that move between procedures** (`.proc`, `.endproc`, `.scope`, `.endscope`, `.segment`): record the procedure the line belongs in, which is the new one for an opening line and the outer one for a closing line.
- **Lines with nothing to record** (blank lines, comments): use the nearest earlier recorded line in the same file.

This only adds a lookup; it doesn't change compilation.

## Phase 3: keeping the last good build (language server)

After each successful build, `FileChangeHandler` swaps in a snapshot with:

- the compile state (its names and the line-to-procedure lookup);
- the compiled lines, by source position; and
- the template map, both ways: generated line to `.bmasm` line (already there) and `.bmasm` line to generated lines (new).

The hover reads the snapshot without holding the build lock. A failed build keeps the previous snapshot, so names that only exist in the broken code have no hover until it builds again. Line numbers can be out by however many lines were added or removed since that build; accept that rather than guess.

## Phase 4: the language server's hover

In `HoverHandler`, after the KERNAL lookup:

1. **Find the compiled line(s):**
   - In a `.bmasm` with template code, go through the template map to the generated line. If one source line produced several (a C# loop), use the first; they're nearly always in the same procedure.
   - Inside C# (`@(...)`, `for`), show nothing, or the generated line's result if the cursor is on an `@(...)`.
2. **Find the scope** from Phase 2's lookup.
3. **A name:** look it up from that scope, ignoring visibility (`TryGetValueIgnoringVisibility`). Show:
   - its full path;
   - its type (`byte`, `ushort`, array and length, label, proc, constant, segment);
   - its value in hex and decimal;
   - public or private;
   - for an export, the name or expression it stands for (`AliasOf`).
4. **An instruction operand:** use the value the build already worked out: the compiled `Line`'s operand bytes and addressing mode. This gets `*`, anonymous labels and repeated labels right with no new code. Under it, list each name in the operand with its value, then the expression with the values put in:

   ```
   #COUNT+1  =  $11 (17)   immediate

   COUNT     App:Main:COUNT   constant   $10 (16)
             $10 + 1 = $11
   ```

5. **A directive's expression** (`.const`, `.byte`, `.word`, `.export`, `.constvar`): evaluate it with the build's evaluator, in the line's scope and at the line's address, as `.const` does (`Evaluator.Evaluate(expr, source, variables, address, true)`). Show the same breakdown.
6. **Not found or not worked out** (an unresolved name, a failed build): say so in one line rather than showing nothing, so it's clear the hover is working.

## Phase 5: the extension

Register an `EvaluatableExpressionProvider` for `bmasm` in `extension.ts`, using Phase 1's rule, so VS Code sends the debugger the name or the whole operand rather than its default word. Keep this in step with the language server's helper. As it's TypeScript, it's a port; a shared test list of lines and cursor positions catches the two drifting apart.

## Phase 6: the debugger's hover

- **Turn it on:** set `SupportsEvaluateForHovers = true` in X16D's capabilities. In `ExpressionManager.Evaluate`, handle `context == "hover"` separately from watch and REPL.
- **Scope:** the hover's request carries the frame, not the line, so the extension's provider also needs to pass the line. Either send the qualified name (resolved from the hovered line by the language server's lookup), or add the source position to the expression in a form `Evaluate` understands. Decide which when building it. The first keeps the debugger simple.
- **What it shows:**
  - **A variable:** its current value from memory, typed (byte, ushort, array elements), with its address.
  - **An instruction operand:**
    - the addressing mode;
    - each register it uses, from the current frame;
    - the effective address, with the value there;
    - for `(ptr),y` and `(ptr,x)`, the pointer, the address it holds, and then the target.
  - **Constants and labels:** as the language server shows them.
  - **`$A000` and above:** use the current RAM or ROM bank and say which. A label in a different bank says so instead of showing what happens to be mapped.
- **IO addresses (`$9F00`–`$9FFF`):** show the register's state as the debugger's own views do, read without side effects, or show nothing.
- **Running, not paused:** VS Code doesn't ask, so only the language server's hover shows.

## Phase 7: tests and docs

- **Phase 1:** a table of lines, cursor positions and expected name and operand, used by the C# tests and, if practical, the extension's.
- **Phase 2:** compiler tests that each kind of line (directive, label, blank, inside nested procs, after `.endproc`, after `.endscope`) maps to the right procedure.
- **Phase 4:** language server tests through `HoverHandler` on a small project:
  - a name in another procedure;
  - a private name in another scope;
  - an export;
  - an instruction operand with `<`, `>` and `+1`;
  - a directive;
  - template code;
  - a failed build.
- **Phase 6:** debugger tests through `ExpressionManager`: a variable's live value, an indexed operand, `(ptr),y`, a banked address, and an IO address that must be left unchanged.
- **Docs:** a short section in the language server and debugger pages on what the hover shows in each case, following `BitMagic.Documentation/STYLE.md`.

## Order

Phases 1 to 4 give a useful language server hover on their own. Phase 5 is small. Phase 6 depends on 1, 2 and 5, and is the largest.

## Open questions

1. **Format of the pop-up:** the layout above is a starting point; Markdown tables render poorly in hovers, so it may want to be plain lines.
2. **IO register documentation** (the earlier question): a separate `ITokenDescriptionProvider` for register names and addresses. It fits beside Phase 4 but isn't part of this plan.
3. **Debug hover scope:** pass a qualified name, or pass the source position (Phase 6).
4. **Raw IO state:** check whether the emulator's memory array at `$9F00`–`$9FFF` holds current register values, or whether the debugger's VERA/VIA views read them from elsewhere, before Phase 6 relies on either.
