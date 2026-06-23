# Data Model & Storage Schema: Knowledge Base AI Wizard

We reuse the existing database models to store the generated knowledge base documents and chunks, ensuring absolute compatibility.

## Reuse of Existing Models

### 1. `KnowledgeDocument`

Stores the finalized Q&A set as a single document.
- **Title**: User-defined title (e.g., "السياسات العامة ومعلومات الشحن").
- **Content**: Clean text representation of all approved Q&As formatted with standard headers:
  ```text
  س: ما هي أسعار الشحن وتفاصيل التوصيل؟
  ج: أسعار الشحن 50 جنيهاً لجميع المحافظات والتوصيل خلال 3 أيام عمل.

  س: هل يمكنني استرجاع المنتجات؟
  ج: نعم، يمكنك الاسترجاع خلال 14 يوماً من استلام المنتج بشرط أن يكون بحالته الأصلية.
  ```
- **ProjectId**: Isolation Context.
- **Status**: Draft (requires approval/publishing).

### 2. `KnowledgeChunk`

Stores individual vector searchable chunks.
- **ChunkText**: A complete Q&A block (or multiple Q&A blocks) grouped together.
- **Embedding**: pgvector representation of the `ChunkText`.

## Database Schema Constraints

No new columns or migrations are required.
Data isolation per `ProjectId` is strictly maintained.
