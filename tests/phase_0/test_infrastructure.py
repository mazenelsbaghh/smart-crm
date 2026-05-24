"""
Phase 0: Infrastructure Health Tests

Verifies that all infrastructure services started by Docker Compose
are accessible and correctly configured.

Run with: make test-phase-0
Requires: make up (services must be running)
"""
import pytest
import psycopg2
import redis
import pika
from elasticsearch import Elasticsearch
import boto3
from botocore.client import Config as BotoConfig


class TestPostgreSQL:
    """Test PostgreSQL connectivity and pgvector extension."""

    def test_connection(self, postgres_config):
        """Test that PostgreSQL is accessible and accepts connections."""
        conn = psycopg2.connect(
            host=postgres_config["host"],
            port=postgres_config["port"],
            user=postgres_config["user"],
            password=postgres_config["password"],
            dbname=postgres_config["dbname"],
        )
        assert conn is not None
        conn.close()

    def test_pgvector_extension(self, postgres_config):
        """Test that the pgvector extension is available."""
        conn = psycopg2.connect(
            host=postgres_config["host"],
            port=postgres_config["port"],
            user=postgres_config["user"],
            password=postgres_config["password"],
            dbname=postgres_config["dbname"],
        )
        cur = conn.cursor()
        cur.execute("CREATE EXTENSION IF NOT EXISTS vector;")
        conn.commit()
        cur.execute("SELECT extname FROM pg_extension WHERE extname = 'vector';")
        result = cur.fetchone()
        assert result is not None
        assert result[0] == "vector"
        cur.close()
        conn.close()

    def test_database_exists(self, postgres_config):
        """Test that the configured database exists."""
        conn = psycopg2.connect(
            host=postgres_config["host"],
            port=postgres_config["port"],
            user=postgres_config["user"],
            password=postgres_config["password"],
            dbname=postgres_config["dbname"],
        )
        cur = conn.cursor()
        cur.execute("SELECT current_database();")
        result = cur.fetchone()
        assert result[0] == postgres_config["dbname"]
        cur.close()
        conn.close()


class TestRedis:
    """Test Redis connectivity."""

    def test_ping(self, redis_config):
        """Test that Redis responds to PING."""
        r = redis.Redis(
            host=redis_config["host"],
            port=redis_config["port"],
        )
        assert r.ping() is True

    def test_set_and_get(self, redis_config):
        """Test basic set/get operations."""
        r = redis.Redis(
            host=redis_config["host"],
            port=redis_config["port"],
        )
        r.set("test_key", "test_value")
        value = r.get("test_key")
        assert value == b"test_value"
        r.delete("test_key")


class TestRabbitMQ:
    """Test RabbitMQ connectivity."""

    def test_connection(self, rabbitmq_config):
        """Test that RabbitMQ accepts connections."""
        credentials = pika.PlainCredentials(
            rabbitmq_config["user"],
            rabbitmq_config["password"],
        )
        params = pika.ConnectionParameters(
            host=rabbitmq_config["host"],
            port=rabbitmq_config["port"],
            credentials=credentials,
        )
        connection = pika.BlockingConnection(params)
        assert connection.is_open
        connection.close()

    def test_channel_creation(self, rabbitmq_config):
        """Test that a channel can be created."""
        credentials = pika.PlainCredentials(
            rabbitmq_config["user"],
            rabbitmq_config["password"],
        )
        params = pika.ConnectionParameters(
            host=rabbitmq_config["host"],
            port=rabbitmq_config["port"],
            credentials=credentials,
        )
        connection = pika.BlockingConnection(params)
        channel = connection.channel()
        assert channel.is_open
        channel.close()
        connection.close()

    def test_queue_declare(self, rabbitmq_config):
        """Test that a queue can be declared and deleted."""
        credentials = pika.PlainCredentials(
            rabbitmq_config["user"],
            rabbitmq_config["password"],
        )
        params = pika.ConnectionParameters(
            host=rabbitmq_config["host"],
            port=rabbitmq_config["port"],
            credentials=credentials,
        )
        connection = pika.BlockingConnection(params)
        channel = connection.channel()
        result = channel.queue_declare(queue="test_queue", auto_delete=True)
        assert result.method.queue == "test_queue"
        channel.queue_delete(queue="test_queue")
        channel.close()
        connection.close()


class TestElasticsearch:
    """Test Elasticsearch connectivity."""

    def test_cluster_health(self, elasticsearch_config):
        """Test that Elasticsearch cluster is healthy."""
        es = Elasticsearch(
            f"http://{elasticsearch_config['host']}:{elasticsearch_config['port']}"
        )
        health = es.cluster.health()
        assert health["status"] in ("green", "yellow")
        assert health["cluster_name"] is not None

    def test_index_operations(self, elasticsearch_config):
        """Test basic index create/delete operations."""
        es = Elasticsearch(
            f"http://{elasticsearch_config['host']}:{elasticsearch_config['port']}"
        )
        index_name = "test_smartcore_index"
        # Create index
        if es.indices.exists(index=index_name):
            es.indices.delete(index=index_name)
        es.indices.create(index=index_name)
        assert es.indices.exists(index=index_name)
        # Cleanup
        es.indices.delete(index=index_name)


class TestMinIO:
    """Test MinIO (S3-compatible) connectivity."""

    def test_connection(self, minio_config):
        """Test that MinIO is accessible."""
        s3 = boto3.client(
            "s3",
            endpoint_url=f"http://{minio_config['host']}:{minio_config['port']}",
            aws_access_key_id=minio_config["access_key"],
            aws_secret_access_key=minio_config["secret_key"],
            config=BotoConfig(signature_version="s3v4"),
            region_name="us-east-1",
        )
        # List buckets — should not raise
        response = s3.list_buckets()
        assert "Buckets" in response

    def test_bucket_operations(self, minio_config):
        """Test bucket create/delete operations."""
        s3 = boto3.client(
            "s3",
            endpoint_url=f"http://{minio_config['host']}:{minio_config['port']}",
            aws_access_key_id=minio_config["access_key"],
            aws_secret_access_key=minio_config["secret_key"],
            config=BotoConfig(signature_version="s3v4"),
            region_name="us-east-1",
        )
        bucket_name = "test-smartcore-bucket"
        # Create bucket
        s3.create_bucket(Bucket=bucket_name)
        # Verify it exists
        response = s3.list_buckets()
        bucket_names = [b["Name"] for b in response["Buckets"]]
        assert bucket_name in bucket_names
        # Cleanup
        s3.delete_bucket(Bucket=bucket_name)
