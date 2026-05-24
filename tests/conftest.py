"""
Global pytest configuration and fixtures for Smart Customer Core tests.
"""
import os
import pytest
from dotenv import load_dotenv

# Load environment variables from .env file
load_dotenv()


@pytest.fixture(scope="session")
def postgres_config():
    """PostgreSQL connection configuration from environment."""
    return {
        "host": os.getenv("POSTGRES_HOST", "localhost"),
        "port": int(os.getenv("POSTGRES_PORT", "5432")),
        "user": os.getenv("POSTGRES_USER", "smartcore"),
        "password": os.getenv("POSTGRES_PASSWORD", "changeme_postgres"),
        "dbname": os.getenv("POSTGRES_DB", "smartcustomercore"),
    }


@pytest.fixture(scope="session")
def redis_config():
    """Redis connection configuration from environment."""
    return {
        "host": os.getenv("REDIS_HOST", "localhost"),
        "port": int(os.getenv("REDIS_PORT", "6379")),
    }


@pytest.fixture(scope="session")
def rabbitmq_config():
    """RabbitMQ connection configuration from environment."""
    return {
        "host": os.getenv("RABBITMQ_HOST", "localhost"),
        "port": int(os.getenv("RABBITMQ_PORT", "5672")),
        "user": os.getenv("RABBITMQ_DEFAULT_USER", "admin"),
        "password": os.getenv("RABBITMQ_DEFAULT_PASS", "changeme_rabbitmq"),
        "mgmt_port": int(os.getenv("RABBITMQ_MGMT_PORT", "15672")),
    }


@pytest.fixture(scope="session")
def elasticsearch_config():
    """Elasticsearch connection configuration from environment."""
    return {
        "host": os.getenv("ELASTICSEARCH_HOST", "localhost"),
        "port": int(os.getenv("ELASTICSEARCH_PORT", "9200")),
    }


@pytest.fixture(scope="session")
def minio_config():
    """MinIO connection configuration from environment."""
    return {
        "host": os.getenv("MINIO_HOST", "localhost"),
        "port": int(os.getenv("MINIO_API_PORT", "9000")),
        "access_key": os.getenv("MINIO_ROOT_USER", "minioadmin"),
        "secret_key": os.getenv("MINIO_ROOT_PASSWORD", "changeme_minio"),
        "bucket": os.getenv("MINIO_BUCKET", "smartcore-media"),
    }
