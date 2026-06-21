# Quickstart: UX/UI Unified Inbox Redesign & GSAP Animations

This quickstart describes the steps required to set up the development environment, apply backend migrations, and verify the frontend redesign and animations.

---

## 1. Setup & Installation

### Install Frontend Dependencies
GSAP and `@gsap/react` must be installed inside the Next.js application workspace:
```bash
cd frontend
npm install gsap @gsap/react
```

### Apply Database Migrations
Run the migrations generator inside the backend directory:
```bash
cd backend
dotnet ef migrations add AddCrmExtraFields
```
The migration will be applied automatically when starting the backend project. To apply it manually, run:
```bash
dotnet ef database update
```

---

## 2. Running Local Servers

### Run Backend
Ensure Postgres and RabbitMQ are running, then launch the ASP.NET Core project:
```bash
cd backend
dotnet run
```

### Run Frontend
Start the Next.js development server:
```bash
cd frontend
npm run dev
```
Open [http://localhost:3000/inbox](http://localhost:3000/inbox) in your browser.

---

## 3. Verification

### Build & Compilation Checks
Verify compilation succeeds for both frontend and backend code:
```bash
cd backend
dotnet build

cd ../frontend
npm run build
npm run lint
```
