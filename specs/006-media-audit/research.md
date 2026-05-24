# Technical Research: Shared Assets, Media Engine & Audit Trail

**Date**: 2026-05-25

## 1. File Storage: MinIO Connection and SDK Choice
- **Decision**: Use `AWSSDK.S3` (.NET Amazon S3 SDK) to interact with MinIO.
- **Rationale**: MinIO is fully S3-compatible. The official AWS SDK is highly optimized, standard in the .NET ecosystem, and offers built-in support for signed URL generation with expiry times.
- **Alternatives Considered**: 
  - `Minio` SDK: Standard MinIO package exists, but can be less idiomatic and has fewer documentation examples for signed URL generation compared to the robust `AWSSDK.S3`.

## 2. Image Transformation and WhatsApp Optimization
- **Decision**: Use `SixLabors.ImageSharp` for image operations (resizing, compressing, thumbnail generation).
- **Rationale**: `SixLabors.ImageSharp` is a fully managed, cross-platform image processing library that does not depend on native graphics libraries like `libgdiplus` (which fails on Linux/Docker containers).
- **Alternatives Considered**:
  - `System.Drawing.Common`: Not cross-platform on modern .NET (Windows-only from .NET 6 onwards).
  - `SkiaSharp`: Highly performant but requires installing native platform-specific binaries inside Docker container, introducing deployment complexity.

## 3. Structured Audit Trail & Decisional Logging
- **Decision**: Use `Serilog.AspNetCore` with a JSON file sink, and index/query them in Elasticsearch.
- **Rationale**: Serilog provides structured logs in JSON format, which can be easily parsed and searched. We can use Elasticsearch to index audit trail events and expose a clean query API.
- **Alternatives Considered**:
  - Direct PostgreSQL logging: Slows down database writes and table grows indefinitely. Using Elasticsearch keeps PostgreSQL clean and optimizes full-text searches across log fields.

## 4. System Health and Monitoring
- **Decision**: Implement a Custom System Health Endpoint (`/api/system/health`) querying connection status of PostgreSQL, Redis, RabbitMQ, MinIO, Elasticsearch, and checking WhatsApp session status.
- **Rationale**: Keeps health status checks unified in a single endpoint that outputs clear JSON, allowing the notification system to trigger alerts on threshold breaches.
