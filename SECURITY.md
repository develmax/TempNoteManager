# Security Policy

## Supported Versions

Security fixes are provided for the latest public release and the current `main` branch.

| Version | Supported |
| ------- | --------- |
| Latest release | Yes |
| `main` | Yes |
| Older releases | Best effort |

## Чувствительные данные

TempNoteManager не должен хранить AI API key в репозитории или в `settings.json`.

Ключ хранится в Windows Credential Manager как:

```text
TempNoteManager.AI.ApiKey
```

Перед публикацией issue, логов или скриншотов проверьте, что там нет:

- AI API key.
- Полных приватных путей, если они чувствительны.
- Содержимого личных заметок.
- Копий `%APPDATA%\TempNoteManager\settings.json`.
- Копий `%APPDATA%\TempNoteManager\ai-cache.json`, если в файлах были приватные данные.

## Reporting a Vulnerability

Please report security issues privately instead of opening a public issue.

Recommended disclosure path:

1. Email the repository owner at `develmax@gmail.com`.
2. Include the affected version or commit, a short impact description, and reproduction steps.
3. Do not attach real notes, AI keys, `settings.json`, or `ai-cache.json` if they contain private data.

Expected response:

- Initial acknowledgement: within 7 days.
- Triage update: within 14 days.
- Fix or mitigation plan: depends on severity and reproducibility.

When a fix is ready, it will be published in the repository and, when appropriate, in a GitHub Release.

## Автоматические проверки

В репозитории подключены несколько проверок безопасности:

- CodeQL для статического анализа C# и code scanning alerts.
- Dependency Review для pull request с изменениями зависимостей.
- OpenSSF Scorecard для оценки практик безопасности репозитория.
- Dependabot для регулярного обновления NuGet-зависимостей и GitHub Actions.

Эти проверки помогают поймать часть проблем автоматически, но не заменяют ручной review сценариев, которые двигают, переименовывают или удаляют реальные файлы пользователя.

## Файловые операции

Приложение может физически перемещать, переименовывать и удалять файлы. Ошибки в этих сценариях считаются важными, особенно если они затрагивают:

- Потерю файла.
- Неверное обновление `session.xml` Notepad++.
- Перемещение в неправильную категорию или корзину.
- Повторный AI-анализ без необходимости.

## Как сообщать о проблемах

Создайте приватное сообщение владельцу репозитория, если проблема содержит секреты или личные данные. Для обычных багов можно создать GitHub issue с минимальным воспроизводимым сценарием.
