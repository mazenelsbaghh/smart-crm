# Quickstart Guide: WhatsApp Media & AI Testing

Follow these instructions to verify and test the end-to-end WhatsApp Media & AI features locally:

## 1. Setup Local Environment
Ensure all backend, frontend, gateway, MinIO, Postgres, Redis, and RabbitMQ services are running:
```bash
make up
```

## 2. Verify Storage Bucket
Check that the MinIO console is accessible at `http://localhost:9001` (default access key: `minioadmin`, secret key: `changeme_minio`). Verify that the `smartcore-media` bucket is automatically created by the `MinIoStorageService`.

## 3. Verify Integration Testing via Pytest
We will write automated integration tests under `tests/phase_5/test_whatsapp_media_ai.py` that simulate:
1. Sending a POST upload request with a sample audio file.
2. Mocking a Baileys gateway event with `messageType: "Voice"` and the uploaded `AssetId`.
3. Verifying that the backend receives the webhook, stores the message, transcribes it via Gemini Mock / client, and saves the transcription.
4. Verifying that the pre-signed URL endpoint returns a valid temporary S3 URL.

Run the test suite:
```bash
pytest tests/phase_5/test_whatsapp_media_ai.py
```
