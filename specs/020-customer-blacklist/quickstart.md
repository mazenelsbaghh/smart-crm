# Quickstart Guide: Customer Blacklist Feature

This guide helps setup and verify the Customer Blacklist feature.

## 1. Database Migrations

1. Ensure the PostgreSQL container is running:
   ```bash
   make up
   ```

2. From the `backend` folder, generate the migration:
   ```bash
   dotnet ef migrations add AddIsBlacklistedToCustomer
   ```

3. Restart the backend container or execute migrations:
   ```bash
   make db-migrate
   ```

4. Verify that the table schema has been updated. You can connect to PostgreSQL using `psql` or inspect logs to ensure the migration was applied:
   ```bash
   docker compose logs backend
   ```

## 2. Running the Application

Start all services locally:
```bash
make up
```

The frontend will be accessible at `http://localhost:3002` (forwarded via Nginx at `http://localhost`).

## 3. Testing Blacklist Bypassing

You can run automated integration tests to verify the AI auto-reply bypass.
```bash
make test-phase-2
```
