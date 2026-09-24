#!/bin/sh
# The keystore Mendelson imports on startup (certificates.p12, password "test"): our key pair and the certificate
# of AS24Net; then the server with the launcher.
set -e
cd /opt/mendelson
rm -f certificates.p12
openssl pkcs12 -export -in /certs/mendelson.pem -inkey /certs/mendelson.key -name mendelson \
    -out certificates.p12 -passout pass:test
keytool -importcert -noprompt -alias as24net -file /certs/as24net.pem \
    -keystore certificates.p12 -storetype PKCS12 -storepass test
exec java -Djava.awt.headless=true -Xmx1g -cp ".:$(cat classpath)" InteropLauncher
