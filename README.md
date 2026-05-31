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

## Что изменено в форке vs оригинала

1. Лог — flush каждые 250 мс (было 1000).
2. SOCKS5 прокси — подключение через прокси, кнопка проверки в диалоге.
3. Иконка — корректно отображается в заголовке и taskbar.
4. Поиск в редакторах — поле поиска в Алиасах, Хайлайтах, Заменах, Триггерах.
5. Лор — автохайлайт — известные вещи подсвечиваются прямо в тексте игры.
6. Лор — дроп-локации — автозапись «с какого монстра в какой зоне» при подборе; тултип «Падает с»; команды `лорд` / `лорд удалить N`.
7. Лор — команды — `лорк` удаляет комментарий без аргумента; `лор справка` показывает все команды.
8. Монстры — ID — `$monsteridN`, отображение id1/id2 голубым в виджете, умный матчинг при обновлении списка.
9. Монстры — дефис — `$monster1` для «Пигмей-маг» работает корректно.
10. Монстры — `$monsterLast` — последний монстр в комнате.
11. Эффекты — добавлены: Затуманивание разума, Затуманивание, Слово тьмы, Окостенение, Групповая точность.
12. Эффекты — матчинг без учёта регистра и `_`/пробел; новые эффекты добавляются в настройки автоматически.
13. Маршруты — поиск — в диалогах «Идти в» и «Удалить маршрут».
14. Маршруты — `route goto подстрока` — умный переход по подстроке.
15. Маршруты — генерация — кнопка в диалоге, BFS по всем зонам.
16. Травник — разметка комнат на карте, кросс-зонный обход, Dijkstra, невидимость, база 18 трав, автодетект навыка, сохранение настроек.

## Оригинальный репозиторий

- https://github.com/syrompetka/mudclient

## Release Policy

- Source code stays in this repository.
- Built client artifacts are published as GitHub Releases assets.
