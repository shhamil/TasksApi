# TaskApi

Тестовое задание для позиции .NET Backend Developer (Middle).
Простое API для управления задачами: создание, список, завершение, удаление.
При завершении задачи в RabbitMQ уходит событие `task.completed`.


## Запуск бд и rabbitMq

```bash
docker compose up -d --build
