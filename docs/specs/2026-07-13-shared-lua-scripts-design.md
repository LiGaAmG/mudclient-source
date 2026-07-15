# Shared Lua scripts — design

## Goal

Replace the per-profile script store with one shared `scripts` folder. A Lua file
is a portable script; its adjacent metadata file describes where it runs.

## Files

```text
<client settings folder>/scripts/
  heal.lua
  heal.script.json
```

`heal.lua` contains code only. `heal.script.json` contains only configuration
for `heal.lua`; there is no central scripts registry.

```json
{
  "version": 1,
  "global": false,
  "autoStart": true,
  "profiles": ["Mage", "Warrior"]
}
```

Missing or invalid metadata is treated safely as `global=false`,
`autoStart=false`, and an empty profile list. The UI reports invalid metadata
without preventing the client from starting.

## Assignment and running

- A global script applies to every current and future profile. Its `profiles`
  list is ignored.
- A non-global script applies only to profile names in `profiles`. Profile names
  are stable settings-folder names; temporary tab UIDs are never persisted.
- On profile connection, the client starts every applicable script with
  `autoStart=true`.
- Manual Start and Stop affect only running instances on open profiles. They do
  not alter metadata and do not survive disconnect.
- Turning Global off preserves currently open profiles as explicit assignments,
  so the script is not unexpectedly stopped for them.
- New profiles require no metadata update: global scripts apply by rule.

## Script folder changes

The script manager watches `*.lua` and adjacent `*.script.json` files.

- A new `.lua` appears with safe default metadata.
- Removing a `.lua` removes it from the list and stops its running instances.
- The UI can refresh manually after external edits.
- A changed metadata file takes effect for future connections. Applying it to
  existing open profiles is an explicit UI action.

## Auto-reload

`Auto-reload scripts` is a client-local editor preference, not transferable
script metadata.

- When enabled, saving or externally changing a `.lua` restarts instances that
  were already running.
- It never starts a currently stopped script.
- The replacement code is started before the old instance is stopped. If it
  fails to compile, the existing instance remains alive and the error is logged.

## WinForms UI

The Scripts window and Help window use WinForms. AvalonEdit is hosted through
`ElementHost` for Lua syntax highlighting, line numbers, Ctrl+S, word wrap, and
adjustable font size.

The Scripts window has a file list, Global and Auto-start switches, a profile
checklist, code editor, Start/Stop controls, a local auto-reload switch, and an
event log. Only `.lua` files appear in the file list.

The Help window ports the existing topic catalogue: searchable categories on
the left, text and read-only syntax-highlighted Lua examples on the right. Its
overview is updated for shared files and adjacent metadata.

## Migration

The prior `Profile.Scripts` storage is not used by the new manager. Existing
per-profile scripts require an explicit one-time import/migration decision;
the new flow must not silently overwrite them.
