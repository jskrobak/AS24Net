# Mendelson opensource AS2 (GPL) without its GUI: the server is started by InteropLauncher, which also creates the
# stations of the tests and answers the test runner. Mendelson itself has no headless mode and no API for partners.
FROM eclipse-temurin:21-jdk
ARG MENDELSON_BUILD=1.1b69
RUN apt-get update && apt-get install -y --no-install-recommends curl unzip openssl && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL -o /tmp/mendelson.zip "https://sourceforge.net/projects/mec-as2/files/install_mendelson_opensource_as2_${MENDELSON_BUILD}.zip/download" \
    && mkdir -p /opt/mendelson && unzip -q /tmp/mendelson.zip -d /opt/mendelson && rm /tmp/mendelson.zip
WORKDIR /opt/mendelson
COPY InteropLauncher.java entrypoint.sh ./
RUN CP="as2.jar:jetty10/start.jar:jlib/*:$(find jlib jetty10/lib -type d | sed 's|$|/*|' | paste -sd:)" \
    && echo "$CP" > classpath \
    && javac -cp "$CP" -d . InteropLauncher.java \
    && chmod 755 entrypoint.sh
# 8080 AS2 receiver (/as2/HttpReceiver), 8081 control API of the launcher
EXPOSE 8080 8081
ENTRYPOINT ["/opt/mendelson/entrypoint.sh"]
