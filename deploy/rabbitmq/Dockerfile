# syntax=docker/dockerfile:1.7

# RabbitMQ broker with the delayed message exchange plugin: the Shipment booking saga schedules its
# payment timeout through RabbitMQ delayed delivery, which the stock image does not ship.
FROM rabbitmq:3.13-management-alpine

LABEL org.opencontainers.image.title="parcelflow-rabbitmq" \
      org.opencontainers.image.description="RabbitMQ 3.13 with the delayed message exchange plugin" \
      org.opencontainers.image.source="https://github.com/Yvahajatiana/ShippingTracer"

# Same archive and checksum as the integration tests (tests/Shared/RabbitMqDelayedExchange.cs).
ADD --chown=rabbitmq:rabbitmq --chmod=0644 \
    --checksum=sha256:3479180df03fa830fd59ccf638a60eee8f0e8261524a8616685522d7b8d9a9ee \
    https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.13.0/rabbitmq_delayed_message_exchange-3.13.0.ez \
    /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange-3.13.0.ez

# Alpine security updates not yet picked up by the upstream image, then the plugins.
RUN apk upgrade --no-cache \
 && rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange rabbitmq_prometheus
