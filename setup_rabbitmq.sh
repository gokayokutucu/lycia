#!/bin/bash

# Declare the topic exchange
rabbitmqadmin -H 127.0.0.1 declare exchange name=saga_events_exchange type=topic

# Declare the queues
rabbitmqadmin -H 127.0.0.1 declare queue name=order_service_saga_events_q durable=true
rabbitmqadmin -H 127.0.0.1 declare queue name=inventory_service_order_created_q durable=true
rabbitmqadmin -H 127.0.0.1 declare queue name=inventory_service_saga_events_q durable=true
rabbitmqadmin -H 127.0.0.1 declare queue name=payment_service_stock_reserved_q durable=true
rabbitmqadmin -H 127.0.0.1 declare queue name=payment_service_saga_events_q durable=true
rabbitmqadmin -H 127.0.0.1 declare queue name=delivery_service_payment_processed_q durable=true

# Declare bindings for order_service_saga_events_q
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="order_service_saga_events_q" routing_key="order.stock.reservation_failed"
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="order_service_saga_events_q" routing_key="order.payment.failed"
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="order_service_saga_events_q" routing_key="order.shipment.failed"
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="order_service_saga_events_q" routing_key="order.compensation.order.cancel"

# Declare binding for inventory_service_order_created_q
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="inventory_service_order_created_q" routing_key="order.created"

# Declare bindings for inventory_service_saga_events_q
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="inventory_service_saga_events_q" routing_key="order.payment.failed"
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="inventory_service_saga_events_q" routing_key="order.shipment.failed"
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="inventory_service_saga_events_q" routing_key="order.compensation.inventory.release"

# Declare binding for payment_service_stock_reserved_q
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="payment_service_stock_reserved_q" routing_key="order.stock.reserved"

# Declare bindings for payment_service_saga_events_q
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="payment_service_saga_events_q" routing_key="order.shipment.failed"
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="payment_service_saga_events_q" routing_key="order.compensation.payment.refund"

# Declare binding for delivery_service_payment_processed_q
rabbitmqadmin -H 127.0.0.1 declare binding source="saga_events_exchange" destination_type="queue" destination="delivery_service_payment_processed_q" routing_key="order.payment.processed"
