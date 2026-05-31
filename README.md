# mudclient-source (LiGaAmG fork)

A client for text base RPG games.

## Fork Changes (LiGaAmG)

This fork contains custom gameplay and UX improvements used in private/public releases.

- Version line updated to `1.6.6` in About/installer metadata.
- SOCKS5 proxy support in connection flow:
  - proxy host/port settings
  - connect via SOCKS5
  - proxy test action in connection dialog
- Faster log flushing for external log watcher tools.
- GroupWidget/Monsters improvements:
  - added affects (including Fog, DarkWord)
  - aliases/normalization updates
  - icon/resource mappings
- Lore tooltip pipeline updates in StuffDatabase plugin:
  - better bracketed item parsing
  - count/quality/truncation matching improvements
  - command/help improvements for lore actions
  - drop-location support and related command flow
- Map/route-related custom changes are included in this fork branch line.

## Release Policy

- Source code stays in this repository.
- Built client artifacts are published as GitHub Releases assets.
