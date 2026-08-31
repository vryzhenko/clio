# check_pr Command

Команда для проверки статуса Pull Requests в репозитории clio.

## Описание

Команда `/check_pr` анализирует все открытые Pull Requests в репозитории и предоставляет подробную информацию о:
- Статусе ревью (одобрено, требуются изменения, ожидает ревью)
- Состоянии CI/CD пайплайнов (сборка, тесты, анализ кода)
- Возможности слияния (конфликты, готовность к merge)
- Действиях, необходимых для завершения PR

## Использование

### Базовое использование
```bash
# PowerShell
./check-pr.ps1

# Bash (macOS/Linux)
./check-pr.sh
```

### Параметры

asda
sda
sd
as
das
d
as
das
d
as
- `--author <username>` - Фильтр по автору PR
- `--label <label>` - Фильтр по метке
- `--state <state>` - Состояние PR (open, closed, merged, all)
- `--limit <number>` - Ограничить количество PR
- `--output <file>` - Сохранить отчет в файл
- `--watch` - Режим наблюдения (обновление каждые 30 секунд)

### Примеры

```bash
# Показать все открытые PR
./check-pr.sh

# Показать PR конкретного автора
./check-pr.sh --author kirillkrylov

# Показать последние 5 закрытых PR
./check-pr.sh --state closed --limit 5

# Сохранить отчет в файл
./check-pr.sh --output pr-report.md

# Режим наблюдения
./check-pr.sh --watch
```

## Требования

- GitHub CLI (`gh`) должен быть установлен и аутентифицирован
- Доступ к репозиторию Advance-Technologies-Foundation/clio

## Установка GitHub CLI

### macOS
```bash
brew install gh
```

### Windows
```powershell
winget install --id GitHub.cli
# или
choco install gh
```

### Linux
См. https://github.com/cli/cli/blob/trunk/docs/install_linux.md

## Аутентификация

```bash
gh auth login
```

## Формат отчета

```
📋 PULL REQUESTS REPORT
=====================

🔄 PR #123: Название PR
│
├── 👤 Author: имя автора
├── 📅 Created: дата создания | Updated: дата обновления
├── 🌿 Branch: исходная_ветка → целевая_ветка
├── 📝 Review: статус ревью
├── 🔀 Mergeable: возможность слияния
│
└── 🚀 CI/CD Pipelines:
    ├── 🏗️  Build: статус сборки
    ├── 🧪 Tests: статус тестов
    └── 📊 Analysis: статус анализа

Action Items:
• Список действий для завершения PR

═══════════════════════════════════════════
```

## Статусы и иконки

### Статус ревью
- ✅ Approved - PR одобрен
- ❌ Changes requested - требуются изменения
- ⏳ Pending review - ожидает ревью

### Возможность слияния
- ✅ MERGEABLE - можно сливать
- ❌ CONFLICTING - есть конфликты
- ⚠️ UNKNOWN - статус неизвестен

### CI/CD статусы
- ✅ SUCCESS - успешно
- ❌ FAILURE - ошибка
- ⏸️ SKIPPED/CANCELLED - пропущено/отменено
- ⏳ В процессе

## Автоматизация

Команда может быть использована в CI/CD пайплайнах для автоматического мониторинга состояния PR:

```yaml
- name: Check PR Status
  run: ./check-pr.sh --output pr-status.md
```
