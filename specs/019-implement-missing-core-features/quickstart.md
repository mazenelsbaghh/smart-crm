# Developer Quickstart: Implement Missing Core Features

This document provides commands and instructions to verify the implemented features.

## 1. Backend Migration
To apply the changes to the database model (adding the `ApprovalStatus` field to `KnowledgeDocument`), run the entity framework migrations:
```bash
dotnet ef migrations add AddKnowledgeApprovalStatus --project backend/backend.csproj --startup-project backend/backend.csproj
```
Wait, the project is configured to auto-run migrations on startup! So running the app will automatically sync the DB.

## 2. Running Backend Tests
To test the workflow engine, assignment routing, and knowledge approval backend logic, execute:
```bash
dotnet test backend/backend.csproj
```

## 3. Running Integration Tests
To run Pytest integration tests (if any are added or modified):
```bash
make test-integration
```
