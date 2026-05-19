# TempNoteManager

Desktop-утилита для быстрого разбора большого количества файлов, открытых в Notepad++: временных заметок, постоянных файлов и файлов, разложенных по пользовательским хранилищам-категориям.

Приложение читает список открытых файлов из `session.xml` Notepad++, показывает их в таблице или карточках, дает быстрый предпросмотр, помогает сохранять временные файлы в постоянные, переносить файлы между категориями и использовать AI для кратких описаний, тегов и подсказок по недостающим категориям.

## Возможности

- Чтение открытых файлов Notepad++ из `%APPDATA%\Notepad++\session.xml`.
- Определение временных файлов из backup-хранилища Notepad++.
- Табличный и карточный режимы списка.
- Просмотр полного пути, времени создания, времени изменения и размера файла.
- Горизонтальная прокрутка таблицы для длинных путей.
- Правый предпросмотр полного файла при клике по элементу.
- Всплывающий предпросмотр при наведении.
- Перемещение элементов drag and drop и кнопками вверх/вниз.
- Сохранение нового порядка обратно в `session.xml`.
- Сохранение временного файла как постоянного.
- Конвертация постоянного открытого файла обратно во временный.
- Удаление с подтверждением в системную корзину Windows или в выбранную пользовательскую папку.
- Создание хранилищ-категорий как реальных директорий.
- Перемещение файлов в категории и обратно в общий список с физическим перемещением файла.
- Переименование файлов из таблицы, карточек, категории и панели просмотра.
- Пользовательские цвета категорий и автоматический подбор цвета по описанию.
- AI-summary, AI-теги, AI-классификация файлов по категориям.
- Подсказки AI о том, каких категорий не хватает для текущего набора файлов.
- Кэш AI-анализа, чтобы не тратить запросы повторно после перемещения или переименования файла.
- Хранение AI-ключа в Windows Credential Manager, а не в файле настроек.

## Требования

- Windows 10/11.
- Notepad++ с включенным восстановлением сессии.
- .NET 10 SDK для сборки из исходников.
- Опционально: OpenAI-совместимый AI endpoint для summary, тегов и автокатегоризации.

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

В интерфейсе включите AI и задайте:

- API endpoint, например `https://api.openai.com/v1/chat/completions`.
- Модель.
- API key.

Ключ сохраняется в Windows Credential Manager как `Generic Credential` с target:

```text
TempNoteManager.AI.ApiKey
```

В `settings.json` ключ не сохраняется. Там остаются только настройки вроде endpoint, модели и флага включения AI.

Также можно передать настройки через переменные окружения:

```powershell
$env:OPENAI_API_KEY = "..."
$env:TEMP_NOTE_AI_ENDPOINT = "https://api.openai.com/v1/chat/completions"
$env:TEMP_NOTE_AI_MODEL = "..."
```

Для локального OpenAI-совместимого сервера endpoint может выглядеть так:

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
- `AiAnalysisCacheStore` — кэш summary, тегов и классификации.
- `WindowsCredentialStore` — хранение API key в Windows Credential Manager.
- `CategoryColorService` — подбор и нормализация цветов категорий.
- `FileTextReader` — безопасное чтение текста для preview и AI.

## Разработка

Перед pull request проверьте сборку:

```powershell
dotnet build .\TempNoteManager.csproj -c Release
```

Если менялась логика чтения или записи `session.xml`, дополнительно проверьте сценарий на копии профиля Notepad++ или на тестовой сессии.

## Лицензия

Проект распространяется по лицензии MIT. См. [LICENSE](LICENSE).
