# Quickstart: Group Subscribers Search

This guide explains how to verify the group subscribers search feature in the development environment.

## 1. Run the Frontend Development Server

Navigate to the `frontend/` directory (or workspace root if running via docker compose) and ensure the application is running:

```bash
# From workspace root:
npm run dev
# or from frontend folder:
cd frontend && npm run dev
```

## 2. Navigate to Group Appointments Manager

1. Open the application in your browser (e.g. `http://localhost:3000` or whatever address the dev server runs on).
2. Go to the Settings page.
3. Click on the Group Appointments section.

## 3. Verify Search Functionality

1. Select a group with active subscribers/bookings and click **"المشتركين"** (Participants).
2. Verify that a search bar is displayed above the subscribers list with placeholder **"بحث عن مشترك بالاسم أو الهاتف..."** (Search for a participant by name or phone...).
3. **Test Name Search**: Type a student's name. The list should filter immediately to display only matching participants.
4. **Test Phone Search**: Type a student's phone number. The list should filter immediately to display only matching participants.
5. **Test Empty/No Results State**: Type a random string that doesn't match any participant. A message **"لا توجد نتائج تطابق البحث"** (No results found matching search) should be displayed instead of an empty table.
6. **Test Search Reset**: Clear the search input to verify the full list is restored. Close the subscribers panel, reopen it (or open another group's list), and verify the search input is reset to empty.
