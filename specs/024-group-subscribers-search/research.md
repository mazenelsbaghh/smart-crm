# Research: Group Subscribers Search

This document outlines the architectural and design decisions for implementing the search functionality inside the group subscribers list.

## 1. Client-Side vs Server-Side Search

*   **Decision**: Client-side filtering in React.
*   **Rationale**: The entire list of bookings for the selected group (`selectedGroup.bookings`) is already fetched from the API and stored in memory. The maximum capacity of a group is typically small (under 100 students). Client-side filtering is instantaneous, has zero server overhead, and provides a highly responsive UI without any network lag.
*   **Alternatives Considered**:
    *   *Server-side searching*: Rejected because it requires modifying the backend API `/api/group-appointments`, adding search query parameters, database query filtering, and introduces network latency for each keystroke or a debounce delay.

## 2. UI Layout & Style Consistency

*   **Decision**: Place a search input bar below the subscribers list title and actions, and above the table.
*   **Rationale**: This is the standard UX pattern for list search. It will reuse the `styles.input` class from `settings.module.css` to match the project's glassmorphism and input design.
*   **Alternatives Considered**:
    *   *Floating search icon*: Rejected as it adds cognitive load and extra clicks. A visible text box is much more accessible.

## 3. Search Matching Strategy

*   **Decision**: Search matches trimmed query string against `booking.customerName` (case-insensitive) and `booking.customerPhone` (direct match).
*   **Rationale**: Students are typically searched for by name (e.g. "Ahmed") or by the WhatsApp phone number they used to book. This covers 100% of standard admin workflows.
