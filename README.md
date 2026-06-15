> **Безопасность:** не добавляйте `settings.json` и `.env` в git. API-ключи и пароли не должны попадать в репозиторий.
## Горячие клавиши
| Клавиша | Действие |
|---|---|
| `Ctrl` + `` ` `` | Показать / скрыть терминал PowerShell |
| `Shift` + ПКМ | Контекстное меню Windows для выбранного файла |
## Структура проекта
```
provix/
├── Controls/          # UI-компоненты (панели, терминал)
├── Helpers/           # Вспомогательные утилиты
├── Locales/           # Локализация (ru-RU, en-US)
├── Models/            # Модели данных
├── Services/          # Файловая система, Git, архивы, ИИ
├── Themes/            # Тёмная и светлая тема
├── MainWindow.xaml    # Главное окно
├── build.cmd          # Сборка exe
└── run.cmd            # Быстрый запуск для разработки
```
## Технологии
- **WPF** (.NET 8)
- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — архивы
- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) — шифрование ZIP
- [Vanara](https://github.com/dahall/Vanara) — интеграция с оболочкой Windows
## Сборка вручную (dotnet)
```cmd
dotnet publish FileExplorer.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish
```
---
**Provix** — файловый менеджер с акцентом на удобство разработчика: Git, терминал, IDE и ИИ прямо в проводнике.

## Лицензия

**PolyForm Noncommercial 1.0.0** — использование и кастомизация бесплатны, **продажа запрещена**.

- Полный текст: [LICENSE](LICENSE)
- Кратко по-русски: [LICENSE.ru.md](LICENSE.ru.md)
