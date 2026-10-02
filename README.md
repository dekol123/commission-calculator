# Партнёрские отчисления

Три сервиса на .NET 8 считают и выплачивают комиссии по дереву приглашений. У каждого своя база в одном PostgreSQL. Между сервисами нет брокера: синхронный HTTP и outbox.

| Сервис | Порт | База | Чем владеет |
| --- | --- | --- | --- |
| Users | 8081 | `users_db` | пользователи и связь «кто кого пригласил» |
| Accrual | 8082 | `accrual_db` | события, схема, комиссии, outbox |
| Wallet | 8083 | `wallet_db` | кошельки, входящие начисления, выплаты |

Общий проект `Contracts` содержит только DTO и enum схемы.

## Запуск

Нужны Docker и Docker Compose.

```bash
docker compose up --build
```

Готовность: `GET /health/ready` на каждом порту. Сброс данных: `docker compose down -v`.

Локально без контейнеров сервисов те же строки подключения смотрят на `localhost:5432` (логин и пароль `app`). Порты запуска: 8081, 8082, 8083.

```bash
dotnet test
```

## Формулы

Комиссия пишется только при `Profit > 0` и только предкам владельца события. Уровень 1 — тот, кто пригласил владельца, уровень 2 — его пригласивший, и так до 10. Выше десятого уровня строка не создаётся.

- Линейная: `L × Profit / 100`
- Фибоначчи: `F(L) × Profit / 100`, где `F(1) = 1`, `F(2) = 1`, `F(3) = 2`, `F(4) = 3`, … `F(10) = 55`

Уровни 1 и 2 при Фибоначчи оба получают 1%. Это обычный ряд, не ошибка округления. На полной цепочке из 10 уровней сумма процентов равна 143% прибыли, потолка нет.

Сумма комиссии округляется до 2 знаков, `MidpointRounding.AwayFromZero`. Строка, которая после округления стала 0, не сохраняется. В каждой строке лежит `schema_type` схемы, по которой она посчитана. Переключение схемы меняет только будущие расчёты.

При `Profit = 1000` и трёх предках линейно выходит `10 / 20 / 30`, по Фибоначчи `10 / 10 / 20`.

Схема по умолчанию на пустой базе — `Linear`.

## Дерево

У пользователя не больше одного реферера. `PUT` задаёт или заменяет связь и влияет только на будущие события. `DELETE /users/{id}/referrer` снимает связь. Отказ: самоссылка, цикл, неизвестный участник, глубина больше 10.

Глубина — это число предков. После связи у самого пользователя и у самого глубокого его потомка должно остаться не больше 10 предков. Иначе существующее поддерево можно было бы протолкнуть ниже лимита сменой реферера.

`GET /users/{id}/up` — предки от уровня 1 к корню. `GET /users/{id}/down` — приглашённые рекурсивно, плоский список с уровнем от 1, не глубже 10.

## Деньги на кошельке

На кошелёк попадает только выплаченная комиссия. Баланс — сумма строк выплат, стартовое значение 0. Кошелёк создаётся при первом запросе баланса или при выплате. Событие с `Profit <= 0` сохраняется и видно в списке, комиссий по нему нет.

Выплата идёт фоном, по умолчанию раз в 60 секунд (`Payout__IntervalSeconds`).

1. Wallet забирает входящие сообщения outbox.
2. `POST /internal/payouts/claim` с новым `payoutId`. Accrual переводит ещё не выплаченные комиссии в выплаченные только для этого id.
3. Wallet в своей транзакции пишет выплату и строки. Повтор того же `payoutId` возвращает тот же набор. `commission_id` в строках выплаты уникален, поэтому обрыв между claim и локальной записью не даёт вторую проводку.

## Надёжность

- Повтор `POST /users` с тем же `externalId` возвращает уже созданного пользователя.
- Повтор `POST /events` с тем же телом возвращает сохранённое событие и ничего не доначисляет. Другой `profit` или другой пользователь — `409`.
- Неизвестный пользователь: событие не пишется.
- Открытый circuit breaker к Users на приёме: `503`, строка не создаётся.
- Users ответил ошибкой или таймаутом при ещё закрытом breaker: событие остаётся `Pending` (`202`), воркер досчитывает. Если пользователь так и не найдётся, статус становится `Rejected`, событие остаётся в списке, комиссий нет.
- Пачка комиссий уходит в Wallet через outbox в той же транзакции, что и расчёт. Диспетчер берёт строку `FOR UPDATE SKIP LOCKED`, шлёт `POST /internal/inbox`. Повтор с тем же `messageId` не создаёт вторую пачку. После лимита попыток строка остаётся в статусе `Failed`.
- HTTP-клиенты Accrual → Users, Accrual → Wallet и Wallet → Accrual используют retry и circuit breaker.
- `/health/live` — процесс жив. `/health/ready` — доступна своя база.
- Остановка: фоновые циклы выходят между сообщениями, Kestrel дожидается текущих запросов (`ShutdownTimeout` 20 секунд).
- В логах есть `CorrelationId` (заголовок `X-Correlation-ID`), а на соответствующих шагах `EventExternalId` и `PayoutId`.
- Метрики Prometheus: `GET /metrics`. Счётчики приёма событий, рассчитанных комиссий, суммы выплат, глубина outbox, ошибки HTTP-клиентов.

## Примеры

Цепочка: `root` пригласил `mid`, `mid` пригласил `leaf`.

```bash
curl -s -X POST http://localhost:8081/users -H "Content-Type: application/json" -d "{\"externalId\":\"root\"}"
curl -s -X POST http://localhost:8081/users -H "Content-Type: application/json" -d "{\"externalId\":\"mid\"}"
curl -s -X POST http://localhost:8081/users -H "Content-Type: application/json" -d "{\"externalId\":\"leaf\"}"
curl -s -X PUT http://localhost:8081/users/mid/referrer -H "Content-Type: application/json" -d "{\"referrerExternalId\":\"root\"}"
curl -s -X PUT http://localhost:8081/users/leaf/referrer -H "Content-Type: application/json" -d "{\"referrerExternalId\":\"mid\"}"
```

Линейная схема, `Profit = 1000`. `mid` получает 10, `root` получает 20.

```bash
curl -s -X POST http://localhost:8082/events -H "Content-Type: application/json" -d "{\"externalId\":\"evt-linear\",\"userExternalId\":\"leaf\",\"profit\":1000}"
curl -s http://localhost:8082/events/evt-linear
```

Переключение на Фибоначчи не меняет уже сохранённые строки. Новое событие: `mid` получает 10, `root` получает 10.

```bash
curl -s -X PUT http://localhost:8082/schema -H "Content-Type: application/json" -d "{\"schemaType\":\"Fibonacci\"}"
curl -s -X POST http://localhost:8082/events -H "Content-Type: application/json" -d "{\"externalId\":\"evt-fib\",\"userExternalId\":\"leaf\",\"profit\":1000}"
```

Отрицательная прибыль сохраняется без комиссий. Повтор того же события возвращает прежний расчёт. Повтор с другим `profit` отвечает `409`.

```bash
curl -s -X POST http://localhost:8082/events -H "Content-Type: application/json" -d "{\"externalId\":\"evt-loss\",\"userExternalId\":\"leaf\",\"profit\":-50}"
curl -s http://localhost:8082/users/leaf/events
curl -s -X POST http://localhost:8082/events -H "Content-Type: application/json" -d "{\"externalId\":\"evt-linear\",\"userExternalId\":\"leaf\",\"profit\":1000}"
curl -i -X POST http://localhost:8082/events -H "Content-Type: application/json" -d "{\"externalId\":\"evt-linear\",\"userExternalId\":\"leaf\",\"profit\":1}"
```

Самоссылка отклоняется.

```bash
curl -s -X PUT http://localhost:8081/users/leaf/referrer -H "Content-Type: application/json" -d "{\"referrerExternalId\":\"leaf\"}"
```

После интервала выплаты баланс `mid` равен сумме выплаченных комиссий (10 линейных + 10 по Фибоначчи = 20), баланс `leaf` остаётся 0: владельцу события комиссия не начисляется.

```bash
curl -s http://localhost:8083/wallets/mid
curl -s http://localhost:8083/wallets/mid/payouts
curl -s http://localhost:8083/wallets/leaf
```
