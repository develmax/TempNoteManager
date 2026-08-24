# TempNoteManager

[![Release](https://img.shields.io/github/v/release/develmax/TempNoteManager?label=release)](https://github.com/develmax/TempNoteManager/releases/latest)
[![Build](https://github.com/develmax/TempNoteManager/actions/workflows/dotnet.yml/badge.svg)](https://github.com/develmax/TempNoteManager/actions/workflows/dotnet.yml)
[![CodeQL](https://github.com/develmax/TempNoteManager/actions/workflows/codeql.yml/badge.svg)](https://github.com/develmax/TempNoteManager/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/develmax/TempNoteManager/badge)](https://scorecard.dev/viewer/?uri=github.com/develmax/TempNoteManager)
[![License](https://img.shields.io/github/license/develmax/TempNoteManager)](LICENSE)

Настольная утилита для быстрого разбора большого количества файлов, открытых в Notepad++: временных заметок, постоянных файлов и файлов, разложенных по пользовательским хранилищам-категориям.

Приложение читает список открытых файлов из `session.xml` Notepad++, показывает их в таблице или карточках, дает быстрый предпросмотр, помогает сохранять временные файлы в постоянные, переносить файлы между категориями и использовать AI для кратких описаний, тегов и подсказок по недостающим категориям.

## Документация

- [Руководство пользователя](docs/USER_GUIDE.md)

## Автоматические проверки

- `dotnet` - сборка Release на `windows-latest`.
- `CodeQL` - статический анализ C# и публикация code scanning alerts.
- `Dependency Review` - проверка новых зависимостей в pull request на известные уязвимости.
- `OpenSSF Scorecard` - оценка безопасности репозитория и загрузка SARIF в code scanning.
- `Dependabot` - еженедельные pull request для NuGet и GitHub Actions.

## Возможности

- Чтение открытых файлов Notepad++ из `%APPDATA%\Notepad++\session.xml`.
- Определение временных файлов из backup-хранилища Notepad++.
- Табличный и карточный режимы списка.
- Просмотр полного пути, времени создания, времени изменения и размера файла.
- Горизонтальная прокрутка таблицы для длинных путей.
- Правый предпросмотр полного файла при клике по элементу.
- Всплывающий предпросмотр при наведении.
- Перемещение элементов мышью и кнопками вверх/вниз, включая пачку выбранных файлов.
- Массовые операции по выбранным файлам: удаление, перенос в категорию, блокировка, избранное, AI-разметка.
- Сохранение нового порядка обратно в `session.xml`.
- Сохранение временного файла как постоянного.
- Конвертация постоянного открытого файла обратно во временный.
- Удаление с подтверждением в системную корзину Windows или в выбранную пользовательскую папку.
- Журнал операций и отмена последнего изменения `session.xml`, переименования, переноса и удаления в пользовательскую корзину.
- Создание хранилищ-категорий как реальных директорий.
- Перемещение файлов в категории и обратно в общий список с физическим перемещением файла.
- Переименование файлов из таблицы, карточек, категории и панели просмотра.
- Пользовательские цвета категорий и автоматический подбор цвета по описанию.
- AI-описание, AI-теги, AI-классификация файлов по категориям.
- Подсказки AI о том, каких категорий не хватает для текущего набора файлов.
- Кэш AI-анализа, чтобы не тратить запросы повторно после перемещения или переименования файла.
- Подтверждение AI-разметки с оценкой количества новых запросов и объема.
- Настройка автоматического AI-описания, чтобы отключить AI-запросы при наведении/открытии и использовать только кэш или ручную кнопку.
- Фильтры для неразобранных файлов, дубликатов и избранного.
- Локальные флаги `Блок` и `★`, привязанные к хэшу содержимого файла.
- Проверка последнего GitHub-релиза из приложения.
- Хранение AI-ключа в Windows Credential Manager, а не в файле настроек.

## Требования

- Windows 10/11.
- Notepad++ с включенным восстановлением сессии.
- .NET 10 SDK для сборки из исходников.
- Опционально: OpenAI-совместимый адрес AI API для описаний, тегов и автокатегоризации.

## Сборка

```powershell
dotnet restore
dotnet build .\TempNoteManager.csproj -c Release
```

Запуск из исходников:

```powershell
dotnet run --project .\TempNoteManager.csproj -c Release
```

Публикация portable-сборки, если .NET Runtime уже установлен у пользователя:

```powershell
dotnet publish .\TempNoteManager.csproj -c Release -r win-x64 --self-contained false
```

Публикация self-contained single-file сборки:

```powershell
dotnet publish .\TempNoteManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Артефакты публикации будут в `bin\Release\net10.0-windows\win-x64\publish`.

## Как подключить AI

AI в приложении опционален. Если он выключен или не настроен, менеджер продолжает работать как обычный просмотрщик и сортировщик файлов.

Нажмите `Настройки`, включите AI и задайте:

- Адрес API, например `https://api.openai.com/v1/chat/completions`.
- Модель.
- AI-ключ.

Ключ сохраняется в Windows Credential Manager как `Generic Credential` с именем:

```text
TempNoteManager.AI.ApiKey
```

В `settings.json` ключ не сохраняется. Там остаются только настройки вроде адреса API, модели и флага включения AI.

Также можно передать настройки через переменные окружения:

```powershell
$env:OPENAI_API_KEY = "..."
$env:TEMP_NOTE_AI_ENDPOINT = "https://api.openai.com/v1/chat/completions"
$env:TEMP_NOTE_AI_MODEL = "..."
```

Для локального OpenAI-совместимого сервера адрес API может выглядеть так:

```text
http://localhost:11434/v1/chat/completions
```

## Кэш AI

AI-анализ кэшируется в:

```text
%APPDATA%\TempNoteManager\ai-cache.json
```

Кэш привязан к SHA-256 содержимого файла. Поэтому переименование и перемещение файла не заставляет заново покупать AI-анализ.

Категории версионируются по имени и описанию. Если добавить новую категорию или изменить описание существующей, приложение переанализирует только недостающую часть классификации.

## Где хранятся настройки

```text
%APPDATA%\TempNoteManager\settings.json
%APPDATA%\TempNoteManager\ai-cache.json
%APPDATA%\TempNoteManager\file-state.json
%APPDATA%\TempNoteManager\operation-journal.json
%APPDATA%\TempNoteManager\session-backups\
Windows Credential Manager: TempNoteManager.AI.ApiKey
```

Notepad++ session file:

```text
%APPDATA%\Notepad++\session.xml
```

Перед записью изменений в `session.xml` приложение создает резервную копию рядом с файлом с расширением `.bak`.

## Важные замечания

- Для операций, которые меняют `session.xml`, лучше закрыть Notepad++ заранее.
- Если Notepad++ открыт, он может перезаписать сессию своими текущими данными при закрытии.
- Перемещение в категорию физически перемещает файл в директорию категории.
- Удаление в пользовательскую корзину физически переносит файл в выбранную папку.
- AI может ошибаться в тегах и категориях, поэтому автоматические подсказки стоит воспринимать как черновую сортировку.

## Структура проекта

```text
TempNoteManager.csproj
App.xaml
MainWindow.xaml
Models/
Services/
CategoryDialog.xaml
RenameFileDialog.xaml
SuggestedCategoriesDialog.xaml
```

Основные сервисы:

- `NotepadPlusPlusSessionService` — чтение и обновление `session.xml`.
- `AiSummaryService` — OpenAI-совместимые запросы к AI.
- `AiAnalysisCacheStore` — кэш описаний, тегов и классификации.
- `FileStateStore` — локальные флаги блокировки/избранного по хэшу содержимого.
- `OperationJournalStore` — журнал операций, снимки `session.xml` и отмена операций.
- `UpdateCheckService` — проверка последнего GitHub-релиза.
- `WindowsCredentialStore` — хранение AI-ключа в Windows Credential Manager.
- `CategoryColorService` — подбор и нормализация цветов категорий.
- `FileTextReader` — безопасное чтение текста для предпросмотра и AI.

## Разработка

Перед pull request проверьте сборку:

```powershell
dotnet build .\TempNoteManager.csproj -c Release
```

Если менялась логика чтения или записи `session.xml`, дополнительно проверьте сценарий на копии профиля Notepad++ или на тестовой сессии.

## Лицензия

Проект распространяется по лицензии MIT. См. [LICENSE](LICENSE).
