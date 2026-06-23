# Quickstart & Verification: remove-deals-and-profits

**Created**: 2026-06-23

## Manual Verification Steps

1. **Clean Sidebar**: Log in, look at the sidebar navigation, ensure "مسار الصفقات" is not visible.
2. **Clean Dashboard Stats**: Navigate to `/dashboard`, verify that only "إجمالي العملاء" and "متوسط تقييم العملاء" metrics cards are displayed. "الصفقات المفتوحة" and "الإيراد المغلق" must be absent.
3. **Clean Quick Actions**: On `/dashboard`, verify that the quick action button for "مسار الصفقات" is removed.
4. **Clean Customer Detail Form**: Click on any customer to open the customer details panel, verify that the Budget field and Pipeline Stage dropdown are removed.
5. **No Broken Routing**: Try entering `/crm/pipeline` in the browser bar; verify that it redirects safely to `/dashboard` or `/crm`.
