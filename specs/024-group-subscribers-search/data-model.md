# Data Model: Group Subscribers Search

No database schema, migrations, or data model changes are required for this feature. 

The existing entities will be used as-is:
- `GroupAppointment` (loaded in frontend `groups` state)
- `Booking` (nested under `GroupAppointment.bookings` array, with attributes `id`, `customerName`, `customerPhone`, `customerId`, `createdAt`, `isAttended`, `isPaid`)
