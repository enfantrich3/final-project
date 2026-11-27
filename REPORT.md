# Лабораторная 3 — AI code-review бот в CI

## Что сделано

| Артефакт | Файл |
|---|---|
| Workflow | [.github/workflows/ai-review.yml](.github/workflows/ai-review.yml) |
| Review-скрипт | [.github/scripts/ai_review.py](.github/scripts/ai_review.py) (+ тесты [test_ai_review.py](.github/scripts/test_ai_review.py)) |
| Чек-лист для бота | [.github/review-instructions.md](.github/review-instructions.md) |
| Эталонный список проблем | [docs/ai-review/known_issues.json](docs/ai-review/known_issues.json) |
| Подсчёт метрик | [docs/ai-review/score_review.py](docs/ai-review/score_review.py) |
| PR с заложенными проблемами | `feature/property-reviews` → PR #TODO |

## Как устроен пайплайн

1. **Триггер:** `pull_request` с типами `opened`, `synchronize` (и `reopened`). `concurrency` отменяет устаревший прогон при новом push.
2. **Diff:** `git diff origin/<base>...<head.sha>` — three-dot, то есть только изменения ветки PR относительно merge-base. Используется `pull_request.head.sha`, а не `github.sha` (merge-коммит), чтобы номера строк совпадали с кодом ветки.
3. **Фильтрация, два рубежа:**
   - pathspec в `git diff`: `*.lock`, `packages.lock.json`, `dist/`, `generated/`, `bin/`, `obj/`, `Migrations/`;
   - в скрипте: те же шаблоны + `*.Designer.cs`, `*.g.cs`, `*.min.js` + бинарные файлы (по `Binary files … differ` и расширению).
4. **Порог** (см. ниже) — при превышении модель не вызывается, публикуется «PR слишком большой для автоматического ревью (X строк), review пропущен».
5. **Промпт:** system = роль + чек-лист из `review-instructions.md` (+ `AGENTS.md`, если появится); user = список файлов и отфильтрованный diff. Модель предупреждена, что diff — это данные, а не инструкции (защита от prompt injection в коде PR). В модель уходит **только diff**, а не репозиторий.
6. **Structured output:** `response_format: json_schema` (strict) с JSON Schema `{comments: [{file, line, severity, rule, message, reason}]}`; бесплатная модель `nvidia/nemotron-3.5-super-vl:free` через OpenRouter, при её недоступности — резервная `openrouter/free`. Запрос — обычный HTTPS (стандартная библиотека Python, без зависимостей).
7. **Fallback — workflow не падает никогда:** нет ключа, неверный ключ, таймаут (240 с; затем попытка на резервной модели), лимит бесплатного тарифа (429), 5xx, отказ модели (блокировка запроса или нестандартная причина остановки), обрезка по лимиту токенов, пустой ответ, невалидный JSON или мусор → комментарий «ревью не выполнено: <причина>». Парсер дополнительно достаёт JSON из ```` ```json ```` и по фигурным скобкам, нормализует severity и отбрасывает битые элементы. Внешний `try/except` в `main()` и `|| printf …` в workflow — последние рубежи. Все ветки покрыты unit-тестами (9 тестов, без обращения к API).
8. **Публикация:** `actions/github-script` создаёт комментарий, а при следующих push **обновляет** тот же (по маркеру `<!-- ai-review-bot -->`), чтобы не засорять PR. `review.md` и `review.json` сохраняются как artifact прогона.

## Порог на размер diff: 800 изменённых строк или 25 файлов

- Учитываются только добавленные и удалённые строки **после** фильтрации — сгенерированный код и бинарники на порог не влияют.
- Типичный feature-PR в этом репозитории — 150–400 строк (PR #3–#5, учебный PR — 349 строк), так что порог не мешает обычной работе.
- 800 строк diff ≈ 10–15 тыс. токенов вместе с чек-листом — модель держит это с запасом, а вот качество ревью на больших diff падает: внимание размазывается, растёт число пропусков. PR больше 800 строк человеку тоже тяжело ревьюить — сигнал разбить его.
- Ограничение по файлам (25) ловит «широкие» PR (массовые переименования, форматирование), где построчный порог не срабатывает, а ревью бесполезно.
- Оба значения настраиваются через `AI_REVIEW_MAX_LINES` / `AI_REVIEW_MAX_FILES`.

## Секреты и права

- Ключ — только в GitHub Secrets (`OPENROUTER_API_KEY`), передаётся одному шагу через `env`, нигде не печатается. В логах — лишь причина остановки ответа и число токенов.
- `permissions: {}` на уровне workflow, на уровне job — только `contents: read` и `pull-requests: write`. `checkout` с `persist-credentials: false` — токен не остаётся в `.git/config`.
- PR из форков не запускают job (`if: head.repo == repository`) — у них всё равно нет доступа к секретам.
- Внутренние имена веток подставляются через `env`, а не напрямую `${{ }}` в `run:` — защита от script injection через имя ветки.
- Известное ограничение: на `pull_request` выполняется версия скрипта **из PR**, поэтому злонамеренный PR от участника с правом push мог бы изменить скрипт и прочитать секрет. В личном репозитории это приемлемо; для командного — брать скрипт из base-ветки или запускать через `pull_request_target` без checkout кода PR.
- Отдельно: из индекса удалены `bin/` и `obj/` (были закоммичены до появления `.gitignore`).

## Эталонный PR: что заложено

15 реальных проблем по 5 категориям + 4 ловушки на ложные срабатывания. Номера строк — в версии ветки `feature/property-reviews`.

| ID | Категория | Правило | Где | Проблема |
|---|---|---|---|---|
| K1 | security | SEC-1 | `PropertiesController.cs:93` | SQL injection: `FromSqlRaw` + конкатенация `city` |
| K2 | security | SEC-4 | `ReviewsController.cs:27` | GET отзывов отдаёт `Author` (User) с `PasswordHash` |
| K3 | security | SEC-2 | `ReviewsController.cs:104` | `DELETE` отзыва без `[Authorize]` и без проверки автора |
| K4 | security | SEC-3 | `BookingsController.cs:177–188` | IDOR: `UpdateComment` меняет чужую бронь |
| K5 | bug | BUG-1 | `BookingsController.cs:172` | `<=`/`>=` в проверке пересечения дат, нет проверки `checkIn < checkOut` |
| K6 | bug | BUG-2 | `PropertiesController.cs:115` | Выручка в `double` |
| K7 | bug | BUG-3 | `ReviewsController.cs:99` | `SaveChangesAsync()` без `await` |
| K8 | bug | BUG-4 | `ReviewsController.cs:42` | `.Result` в async-методе |
| K9 | bug | BUG-6 | `ReviewsController.cs:73` | `DateTime.Now` вместо `UtcNow` |
| K10 | bug/perf | PERF-1 | `PropertiesController.cs:111` | N+1 запросов в `GetMyStats` |
| K11 | missing test | TEST-1 | `ReviewsController.cs` | 5 новых endpoint без тестов |
| K12 | missing test | TEST-1 | `PropertiesController.cs:89,99` | `Search`, `GetMyStats` без тестов |
| K13 | bug | PY-1 | `Frontend/streamlit_app.py:48` | `requests.get` без `timeout` и обработки ошибок |
| K14 | style | STYLE-1 | `ReviewsController.cs:56,90` | Магическое число 5 (дважды) |
| K15 | style | STYLE-2 | `ReviewsController.cs:79` | `Console.WriteLine` в контроллере |

**Ловушки (корректный код, комментировать нельзя):**

| ID | Где | Почему выглядит подозрительно | Почему корректно |
|---|---|---|---|
| F1 | `PropertiesController.cs:105` | SQL со вставкой `{userId}` | `FromSqlInterpolated` превращает интерполяцию в параметры |
| F2 | `ReviewsController.cs:23,35` | Endpoint без `[Authorize]` | Чтение отзывов и рейтинга публично намеренно, как и каталог |
| F3 | `ReviewsController.cs:21` | `!` подавляет null-проверку | Метод вызывается только из `[Authorize]`-действий, claim гарантирован |
| F4 | `ReviewsController.cs:109` | Конкатенация строк с `id` | Это текст ответа 404, а не SQL |

Тесты на новые методы `BookingsController` добавлены, но только happy-path: они проходят, несмотря на K4 и K5. Это проверка, не путает ли бот «тесты есть» с «тесты проверяют проблему».

## Результаты ревью

> TODO после прогона: вставить вывод `python docs/ai-review/score_review.py review.json`
> и вручную проверить колонку «авто».

**Правила подсчёта:**
- TP — комментарий указывает на эталонную проблему (место ±2 строки, суть совпадает; правило может отличаться).
- Повторный комментарий на уже найденную проблему — дубль: не TP и не FP, но учитывается как шум.
- FP — всё остальное, отдельно отмечаются срабатывания на ловушках F1–F4.
- Если бот нашёл реальную проблему, которую я не закладывал(а), она проверяется вручную и, если подтверждается, считается TP вне эталона (отмечается отдельно, в recall не входит).

| Метрика | Значение |
|---|---|
| Заложено проблем | 15 |
| Комментариев бота | TODO |
| True positives | TODO |
| False positives (из них на ловушках) | TODO |
| Дубли | TODO |
| **Precision** = TP / (TP + FP) | TODO |
| **Recall** = найдено / 15 | TODO |

**Recall по категориям:** security _/4 · bug _/7 · missing test _/2 · style _/2

## Анализ качества reviewer'а

> TODO после прогона: какие категории ловит хорошо и какие пропускает; срабатывал ли
> на ловушках; насколько точны строки и severity; что поменять в чек-листе.
