# Config image'ın içine gömülür; bind mount gerektirmez (Windows'ta dosya paylaşımı sorunları yaşanmaz).
FROM otel/opentelemetry-collector-contrib:0.120.0
COPY otel-collector.yaml /etc/otelcol/config.yaml
