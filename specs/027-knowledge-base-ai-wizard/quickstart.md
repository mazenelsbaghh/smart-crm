# Quickstart Guide: Knowledge Base AI Wizard

This guide describes how to run and manually verify the Knowledge Base AI Wizard.

## Frontend Setup

1. Navigate to the `frontend` directory:
   ```bash
   cd frontend
   ```
2. Start the Vite/Next.js dev server:
   ```bash
   npm run dev
   ```

## Backend Setup

1. Navigate to the `backend` directory:
   ```bash
   cd backend
   ```
2. Build and run the ASP.NET Core project:
   ```bash
   dotnet run --project src/Apps/SmartCrm.Api
   ```

## Manual Verification Steps

1. Click on **"قاعدة المعرفة"** in the sidebar.
2. Enter raw text in the input area (e.g., "محل عطور فخم بالرياض، شحن مجاني للمشتريات فوق 300 ريال").
3. Click **"بدء معالج الأسئلة والأجوبة"**.
4. The wizard stepper should open. Answer the clarifying questions using the suggested buttons or typing custom text.
5. In the final step, click **"توليد الأسئلة والأجوبة"**.
6. Review the list of generated Q&A pairs, edit one of them, and click **"حفظ ونشر"**.
7. Check the document grid. It should display the newly created document with status **"Draft"** or **"Approved"**.
8. Verify in the database (or backend logs) that the chunks were split cleanly at Q&A boundaries.
