import pytest
import httpx
import uuid
import time
import pika
import json

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_message_aggregation_flow(rabbitmq_config):
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    
    # 1. Setup RabbitMQ consumer to catch the published event
    credentials = pika.PlainCredentials(rabbitmq_config["user"], rabbitmq_config["password"])
    params = pika.ConnectionParameters(
        host=rabbitmq_config["host"],
        port=rabbitmq_config["port"],
        credentials=credentials
    )
    
    connection = pika.BlockingConnection(params)
    channel = connection.channel()
    
    queue_name = "MessageAggregatedEvent_test_queue"
    # Declare exchange and queue to ensure they exist for testing
    channel.exchange_declare(exchange="smartcore_exchange", exchange_type="direct", durable=True)
    channel.queue_declare(queue=queue_name, durable=True, exclusive=False, auto_delete=False)
    channel.queue_bind(queue=queue_name, exchange="smartcore_exchange", routing_key="MessageAggregatedEvent")
    channel.queue_purge(queue=queue_name)

    async with httpx.AsyncClient(timeout=10.0) as client:
        # Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "AggregatorTestProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # Send 3 webhook messages in rapid succession (0.5s apart)
        contents = ["Hello!", "I have a question", "regarding smart pricing."]
        for text in contents:
            webhook_resp = await client.post(
                f"{BASE_URL}/webhooks/whatsapp/message",
                json={
                    "projectId": proj_id,
                    "messageId": f"msg_{uuid.uuid4().hex}",
                    "sender": sender_phone,
                    "content": text,
                    "messageType": "Text",
                    "timestamp": int(time.time())
                }
            )
            assert webhook_resp.status_code == 200
            time.sleep(0.5)

        # 2. Get event from RabbitMQ (Wait up to 12 seconds: 5s silence window + buffer)
        received_event = None
        start_time = time.time()
        while time.time() - start_time < 12.0:
            method_frame, header_frame, body = channel.basic_get(queue=queue_name, auto_ack=True)
            if method_frame:
                evt = json.loads(body.decode())
                if evt.get("ProjectId") == proj_id:
                    received_event = evt
                    break
            time.sleep(0.5)

        connection.close()

        # Assert event was successfully generated and received
        assert received_event is not None
        assert received_event["ProjectId"] == proj_id
        assert received_event["Sender"] == sender_phone
        
        # Verify content contains all three messages separated by newlines
        expected_content = "\n".join(contents)
        assert received_event["Content"] == expected_content
