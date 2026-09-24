# OpenAS2 from its release zip on Java 21 (the image of the project itself is still on Java 11, too old for 4.x).
FROM eclipse-temurin:21-jre
ARG OPENAS2_VERSION=4.12.0
RUN apt-get update && apt-get install -y --no-install-recommends curl unzip openssl && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL -o /tmp/openas2.zip "https://sourceforge.net/projects/openas2/files/OpenAS2Server-${OPENAS2_VERSION}.zip/download" \
    && mkdir -p /opt/openas2 && unzip -q /tmp/openas2.zip -d /opt/openas2 && rm /tmp/openas2.zip \
    && chmod 755 /opt/openas2/bin/*.sh
ENV OPENAS2_BASE=/opt/openas2 OPENAS2_HOME=/opt/openas2 OPENAS2_TMPDIR=/opt/openas2/temp
COPY entrypoint.sh /entrypoint.sh
RUN chmod 755 /entrypoint.sh
WORKDIR /opt/openas2
# 10080 AS2 receiver, 10081 receiver of asynchronous MDNs
EXPOSE 10080 10081
ENTRYPOINT ["/entrypoint.sh"]
