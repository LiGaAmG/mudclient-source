# Lore settings persistence

## Scope

Persist the three runtime lore switches in the lore database directory:

- `лор вкл/выкл`;
- `лор цвет вкл/выкл`;
- `лор дроп вкл/выкл`.

## Storage

Use `items_lore_setting.cfg`, with one `key=value` pair per line:

```text
lore_enabled=true
lore_highlight_enabled=true
lore_drop_enabled=true
```

Missing or invalid values keep their default (`true`).

## Compatibility

If the new file is absent, read the old `_color.cfg` to retain the existing
highlight preference, then write the new configuration file. The legacy file
is left untouched.

## Behaviour

Load settings when the lore unit is created. Each of the three lore switch
commands writes the complete configuration immediately after changing its
flag.
