# Adan.AI.Host

Сайдкар-процесс для запуска локальной LLM. Запускается автоматически плагином `Adan.Client.Plugins.AI`.

## Требования

- .NET 8 Runtime (x86)
- Файл модели в формате GGUF

## Протокол

Общается через именованный пайп (JSON-строки).

Запрос генерации:
```json
{"id":"guid","prompt":"текст","max_tokens":200}
```

Ответ:
```json
{"id":"guid","text":"ответ","done":true}
```

Управляющие команды:
```json
{"cmd":"load","model_path":"...","context_size":4096,"threads":4}
{"cmd":"unload"}
{"cmd":"status"}
```
